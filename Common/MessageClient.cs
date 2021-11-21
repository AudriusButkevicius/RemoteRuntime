using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace RemoteRuntime
{
    public class MessageClient : IDisposable
    {
        private readonly PipeStream _pipe;

        private readonly Dictionary<MessageType, Func<IMessage>> _registeredMessages = new();

        private MessageClient(PipeStream pipe)
        {
            Contract.Requires(pipe != null);

            _pipe = pipe;
        }

        public static MessageClient CreateClient(int pid)
        {
            var pipe = new NamedPipeClientStream(".", $"remoteruntime-{pid}", PipeDirection.InOut);
            pipe.Connect();
            pipe.ReadMode = PipeTransmissionMode.Message;

            var client = new MessageClient(pipe);

            client.RegisterMessage<LoadAndRunRequest>();
            client.RegisterMessage<StatusWithError>();

            return client;
        }

        public static MessageClient CreateServer(NamedPipeServerStream pipe)
        {
            var client = new MessageClient(pipe);

            client.RegisterMessage<LoadAndRunRequest>();
            client.RegisterMessage<StatusWithError>();

            return client;
        }

        public IntPtr Id => _pipe.SafePipeHandle.DangerousGetHandle();

        public void Dispose()
        {
            _pipe?.Dispose();
        }

        private void RegisterMessage<T>() where T : IMessage, new()
        {
            IMessage MessageCreator()
            {
                return new T();
            }

            _registeredMessages.Add(MessageCreator().MessageType, MessageCreator);
        }

        public long Poll()
        {
            uint avail = 0;
            if (!Native.PeekNamedPipe(
                _pipe.SafePipeHandle, null, 0, IntPtr.Zero, ref avail, IntPtr.Zero
            ))
            {
                return -1;
            }

            return avail;
        }

        public IMessage Receive()
        {
            using var ms = new MemoryStream();
            byte[] buffer = new byte[256];
            do
            {
                int length = _pipe.Read(buffer, 0, buffer.Length);
                ms.Write(buffer, 0, length);
            } while (!_pipe.IsMessageComplete);

            ms.Position = 0;

            using var br = new BinaryReader(ms, Encoding.Unicode);
            var type = (MessageType)br.ReadInt32();

            if (!_registeredMessages.TryGetValue(type, out Func<IMessage> createFn))
            {
                return null;
            }

            IMessage message = createFn();
            message.ReadFrom(br);
            return message;
        }

        public void Send(IMessage message)
        {
            Contract.Requires(message != null);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.Unicode);

            bw.Write((int)message.MessageType);
            message.WriteTo(bw);

            byte[] buffer = ms.ToArray();
            _pipe.Write(buffer, 0, buffer.Length);
        }
    }
}
