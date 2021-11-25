using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RemoteRuntime
{
    public sealed class RedirectingTextWriter : TextWriter
    {
        private Queue<string> _queue;
        private StringBuilder _builder;
        private char[] _newLineChars;

        public RedirectingTextWriter(Queue<string> queue)
        {
            _queue = queue;
            _builder = new StringBuilder();
            _newLineChars = NewLine.ToCharArray();
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
