using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace RemoteRuntime
{
    public sealed class RedirectingTextWriter : TextWriter
    {
        private readonly ConcurrentQueue<string> _queue;
        private readonly StringBuilder _builder;
        private readonly TextWriter _tee;

        public RedirectingTextWriter(ConcurrentQueue<string> queue, TextWriter tee)
        {
            _queue = queue;
            _builder = new StringBuilder();
            _tee = tee;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _builder.Append(value);
            if (value == '\n')
            {
                Flush();
            }
        }

        public override void Flush()
        {
            var line = _builder.ToString();
            _builder.Clear();
            _tee?.Write(line);
            _tee?.Flush();
            _queue.Enqueue(line);
        }
    }
}
