using System;
using System.Diagnostics.Contracts;
using System.IO;

namespace RemoteRuntime
{
    public enum MessageType
    {
        LoadAndRunRequest = 1,
        LogLine = 2,
        StatusWithError = 3,
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

        public LoadAndRunRequest()
        {
        }

        public LoadAndRunRequest(string path)
        {
            Path = path;
        }

        public void ReadFrom(BinaryReader reader)
        {
            Path = reader.ReadString();
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Path);
        }
    }

    public class LogLine : IMessage
    {
        public MessageType MessageType => MessageType.LogLine;
        public string Line { get; private set; }

        public LogLine()
        {
        }

        public LogLine(string line)
        {
            Line = line;
        }

        public void ReadFrom(BinaryReader reader)
        {
            Line = reader.ReadString();
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Line);
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
