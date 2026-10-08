using System;

namespace Hdr.Html
{
    // Profile errors are explicit so a caller can preserve its previously accepted document.
    public sealed class HtmlParseException : ArgumentException
    {
        public readonly string Code;
        public readonly int Offset;

        public HtmlParseException(string code, string message, int offset)
            : base(HtmlDocument.Profile + ": " + message + " (" + code + ", offset " + offset + ").")
        {
            Code = code;
            Offset = offset;
        }
    }
}
