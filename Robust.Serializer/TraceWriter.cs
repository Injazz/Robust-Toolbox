using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Robust.Shared.Serialization
{

    public class TraceWriter
        : TextWriter
    {

        public override void Write(char[] buffer, int index, int count)
            => Trace.Write(new string(buffer, index, count));
        public override void WriteLine(char[] buffer, int index, int count)
            => Trace.WriteLine(new string(buffer, index, count));

        public override void Write(string value)
            => Trace.Write(value);

        public override void WriteLine()
            => Trace.WriteLine("");

        public override void WriteLine(string value)
            => Trace.WriteLine(value);

        public override Encoding Encoding
            => Encoding.Default;

    }

}
