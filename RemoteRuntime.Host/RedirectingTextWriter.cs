using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteRuntime
{
    public sealed class RedirectingTextWriter : TextWriter
    {
        private ConcurrentQueue<string> _queue;
        private StringBuilder _builder;

        public RedirectingTextWriter(ConcurrentQueue<string> queue)
        {
            _queue = queue;
            _builder = new StringBuilder();
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
            _queue.Enqueue(_builder.ToString());
            _builder.Clear();
        }
    }
}
