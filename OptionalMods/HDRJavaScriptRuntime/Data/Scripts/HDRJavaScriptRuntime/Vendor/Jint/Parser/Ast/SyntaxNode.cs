

namespace Jint.Parser.Ast
{
    public class SyntaxNode
    {
        public SyntaxNodes Type;
        public int[] Range;
        public Location Location;

        
        public T As<T>() where T : SyntaxNode
        {
            return (T)this;
        }
    }
}
