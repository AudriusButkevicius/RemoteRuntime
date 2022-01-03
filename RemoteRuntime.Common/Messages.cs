using System;
using System.Collections.Generic;
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
        public Dictionary<string, string> Arguments { get; private set; }

        public LoadAndRunRequest()
        {
        }

        public LoadAndRunRequest(string path, Dictionary<string, string> arguments = null)
        {
            Path = path;
            Arguments = arguments ?? new Dictionary<string, string>();
        }

        public void ReadFrom(BinaryReader reader)
        {
            Path = reader.ReadString();
            int count = reader.ReadInt32();
            Arguments = new Dictionary<string, string>();
            for (int i = 0; i < count; i++)
            {
                Arguments[reader.ReadString()] = reader.ReadString();
            }
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Path);
            writer.Write(Arguments.Count);
            foreach (var keyValuePair in Arguments)
            {
                writer.Write(keyValuePair.Key);
                writer.Write(keyValuePair.Value);
            }
        }
    }

    public class LogLine : IMessage
    {
        public MessageType MessageType => MessageType.LogLine;
        public string Line { get; private set; }
        public bool IsError { get; private set; }

        public LogLine()
        {
        }

        public LogLine(string line, bool isError)
        {
            Line = line;
            IsError = isError;
        }

        public void ReadFrom(BinaryReader reader)
        {
            Line = reader.ReadString();
            IsError = reader.ReadBoolean();
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Line);
            writer.Write(IsError);
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
