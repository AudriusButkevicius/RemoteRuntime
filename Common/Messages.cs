using System;
using System.Diagnostics.Contracts;
using System.IO;

namespace RemoteRuntime
{
    public enum MessageType
    {
        LoadAndRunRequest = 1,
        StatusWithError = 2,
    }

    public interface IMessage
    {
        MessageType MessageType { get; }
        void ReadFrom(BinaryReader reader);
        void WriteTo(BinaryWriter writer);
    }

    [ContractClassFor(typeof(IMessage))]
    internal class MessageContract : IMessage
    {
        public MessageType MessageType => throw new NotImplementedException();

        public void ReadFrom(BinaryReader reader)
        {
            Contract.Requires(reader != null);

            throw new NotImplementedException();
        }

        public void WriteTo(BinaryWriter writer)
        {
            Contract.Requires(writer != null);

            throw new NotImplementedException();
        }
    }

    public class LoadAndRunRequest : IMessage
    {
        public MessageType MessageType => MessageType.LoadAndRunRequest;
        public string Path { get; private set; }
        public string TypeName { get; private set; }

        public LoadAndRunRequest()
        {
        }

        public LoadAndRunRequest(string path, Type type)

        {
            Path = path;
            TypeName = type.FullName;
        }

        public void ReadFrom(BinaryReader reader)
        {
            Path = reader.ReadString();
            TypeName = reader.ReadString();
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Path);
            writer.Write(TypeName);
        }
    }

    public class StatusWithError : IMessage
    {
        public StatusWithError()
        {
        }

        public StatusWithError(bool success, string error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; private set; }
        public string Error { get; private set; }
        public MessageType MessageType => MessageType.StatusWithError;

        public void ReadFrom(BinaryReader reader)
        {
            Success = reader.ReadBoolean();
            Error = reader.ReadString();
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Success);
            writer.Write(Error);
        }
    }
}
