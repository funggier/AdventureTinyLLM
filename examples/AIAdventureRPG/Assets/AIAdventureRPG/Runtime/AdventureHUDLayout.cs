using UnityEngine;

namespace AIAdventureRPG
{
    // Presentation-only viewport calculation. Everything is measured in
    // reference GUI units; the same scale applies to fonts, spacing and panels.
    public struct AdventureHUDLayout
    {
        public float scale, width, height;
        public Rect stats, selector, log, controller, sign, dialogue, hints;

        public static AdventureHUDLayout ForScreen(float screenWidth, float screenHeight)
        {
            var size=new AdventureHUDLayout();
            size.scale=Mathf.Max(.1f,Mathf.Min(
                screenWidth/1600f,screenHeight/900f));
            size.width=screenWidth/size.scale;
            size.height=screenHeight/size.scale;
            const float margin=16f;
            float statsW=360f;
            float rightW=455f;
            size.stats=new Rect(margin,margin,statsW,475f);
            size.selector=new Rect((size.width-650f)*.5f,margin,650f,113f);
            size.log=new Rect(size.width-rightW-margin,margin,rightW,177f);
            size.controller=new Rect(size.width-rightW-margin,
                size.height-392f-margin,rightW,392f);
            size.sign=new Rect((size.width-550f)*.5f,size.height-151f,550f,124f);
            size.dialogue=new Rect((size.width-540f)*.5f,size.height-167f,540f,140f);
            size.hints=new Rect((size.width-530f)*.5f,size.height-45f,530f,28f);
            return size;
        }
    }
}
