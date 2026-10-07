using VRageMath;
namespace HDRClientRenderer
{
    internal static class PortalActiveRectangle
    {
        internal static bool TryCreate(Vector2I bucket,int width,int height,int gutter,out int drawWidth,out int drawHeight)
        {
            drawWidth=drawHeight=0;if(width<1||height<1||gutter<0||gutter>2||bucket.X!=bucket.Y||bucket.X<256||bucket.X>2048||(bucket.X&(bucket.X-1))!=0)return false;
            long w=(long)width+2*gutter,h=(long)height+2*gutter;if(w>bucket.X||h>bucket.Y)return false;drawWidth=(int)w;drawHeight=(int)h;return true;
        }
    }
}
