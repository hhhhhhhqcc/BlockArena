using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Tiny runtime sprites keep the native build self-contained. No external textures or fonts are copied.
    public static class ArenaIcons
    {
        static readonly Dictionary<string,Texture2D> cache=new Dictionary<string,Texture2D>();
        static readonly Color32 ink=new Color32(27,31,32,255),light=new Color32(228,232,212,255),gold=new Color32(232,182,74,255);
        sealed class Canvas
        {
            public readonly Color32[] pixels=new Color32[32*32];
            public void Box(int x,int y,int w,int h,Color color)
            {
                Color32 c=color;
                for(int yy=Math.Max(0,y);yy<Math.Min(32,y+h);yy++)for(int xx=Math.Max(0,x);xx<Math.Min(32,x+w);xx++)pixels[(31-yy)*32+xx]=c;
            }
            public void Line(int x,int y,int tx,int ty,Color color,int thickness=2)
            {
                int dx=Math.Abs(tx-x),sx=x<tx?1:-1,dy=-Math.Abs(ty-y),sy=y<ty?1:-1,error=dx+dy;
                for(;;){Box(x,y,thickness,thickness,color);if(x==tx&&y==ty)break;int e=error*2;if(e>=dy){error+=dy;x+=sx;}if(e<=dx){error+=dx;y+=sy;}}
            }
        }
        static Texture2D Texture(string key,Action<Canvas> paint)
        {
            Texture2D texture;
            if(cache.TryGetValue(key,out texture))return texture;
            Canvas c=new Canvas();paint(c);
            texture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Arena pixel "+key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
            texture.SetPixels32(c.pixels);texture.Apply(false,true);cache[key]=texture;return texture;
        }
        public static void Draw(Rect r,string icon)
        {
            Texture2D texture=Texture("item:"+icon,c=>PaintItem(c,icon));
            Color old=GUI.color;GUI.color=Color.white;GUI.DrawTexture(r,texture,ScaleMode.ScaleToFit,true);GUI.color=old;
        }
        public static void Creature(Rect r,string kind,Color color)
        {
            string id=(kind??"zombie").ToLowerInvariant().Replace("_","").Replace("-","");
            string model=CreatureModel(id);
            Texture2D texture=Texture("mob:"+model+ColorUtility.ToHtmlStringRGB(color),c=>PaintCreature(c,model,color));
            Color old=GUI.color;GUI.color=Color.white;GUI.DrawTexture(r,texture,ScaleMode.ScaleToFit,true);GUI.color=old;
        }
        public static Texture2D Stone
        {
            get{return Texture("stone",c=>{
                for(int y=0;y<32;y++)for(int x=0;x<32;x++){
                    int n=(x*37+y*61+x*y*3)%13;
                    c.Box(x,y,1,1,new Color((34+n)/255f,(40+n)/255f,(43+n)/255f));
                }
                c.Line(2,9,12,9,new Color(.22f,.25f,.26f),1);c.Line(12,9,16,13,new Color(.12f,.14f,.15f),1);
                c.Line(22,24,29,24,new Color(.24f,.26f,.27f),1);c.Line(22,24,19,21,new Color(.1f,.13f,.14f),1);
            });}
        }
        static Texture2D disc;
        public static Texture2D Disc
        {
            get {
                if(disc!=null)return disc;
                disc=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Arena soft corner",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
                var pixels=new Color[32*32];
                for(int y=0;y<32;y++)for(int x=0;x<32;x++){float distance=Mathf.Sqrt((x-15.5f)*(x-15.5f)+(y-15.5f)*(y-15.5f));pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01(16-distance));}
                disc.SetPixels(pixels);disc.Apply(false,true);return disc;
            }
        }
        static void PaintItem(Canvas c,string icon)
        {
            Color soul=new Color(.3f,.91f,.87f),steel=new Color(.69f,.79f,.82f),wood=new Color(.46f,.27f,.14f);
            switch(icon)
            {
                case "settings":
                    c.Box(12,2,8,28,steel);c.Box(2,12,28,8,steel);c.Box(6,6,20,20,steel);c.Box(10,8,12,16,soul);c.Box(8,10,16,12,soul);c.Box(12,12,8,8,ink);c.Box(14,14,4,4,light);break;
                case "music":
                    c.Box(13,4,3,20,steel);c.Box(13,4,13,4,steel);c.Box(23,4,3,16,steel);c.Box(6,21,10,7,soul);c.Box(17,17,9,7,gold);break;
                case "sound":
                    c.Box(4,12,6,10,steel);c.Box(10,8,5,18,steel);c.Box(14,5,4,24,light);c.Line(21,10,24,13,soul,2);c.Line(24,13,24,20,soul,2);c.Line(24,20,21,23,soul,2);c.Line(25,5,29,10,gold,2);c.Line(29,10,29,24,gold,2);break;
                case "coin":
                    c.Box(9,3,14,3,ink);c.Box(5,6,22,20,ink);c.Box(9,26,14,3,ink);c.Box(9,5,14,22,gold);c.Box(7,8,18,15,gold);c.Box(10,8,12,3,new Color(1,.89f,.46f));c.Box(11,12,3,9,new Color(.65f,.37f,.1f));c.Box(17,10,3,11,new Color(1,.86f,.35f));break;
                case "heart":
                    c.Box(4,7,10,13,ink);c.Box(18,7,10,13,ink);c.Box(8,12,16,13,ink);c.Box(12,24,8,4,ink);c.Box(6,9,7,10,new Color(.94f,.27f,.29f));c.Box(19,9,7,10,new Color(.94f,.27f,.29f));c.Box(10,13,12,9,new Color(.94f,.27f,.29f));c.Box(14,22,4,3,new Color(.75f,.12f,.19f));c.Box(8,10,3,4,new Color(1,.58f,.52f));break;
                case "fire":
                    c.Box(13,3,5,9,new Color(1,.76f,.22f));c.Box(9,10,14,15,new Color(.95f,.32f,.13f));c.Box(5,15,22,10,new Color(.95f,.32f,.13f));c.Box(11,17,11,11,new Color(1,.68f,.15f));c.Box(15,20,5,8,new Color(1,.92f,.48f));break;
                case "poison":
                    c.Box(12,3,9,5,wood);c.Box(14,8,5,6,steel);c.Box(7,14,19,13,steel);c.Box(9,16,15,10,new Color(.4f,.77f,.2f));c.Box(11,20,3,3,new Color(.73f,1,.36f));c.Box(19,17,3,3,new Color(.73f,1,.36f));break;
                case "frost":
                    c.Line(15,3,15,28,soul,3);c.Line(4,8,26,22,soul,3);c.Line(4,22,26,8,soul,3);c.Box(13,12,7,7,light);break;
                case "stun":
                    c.Box(14,3,4,24,gold);c.Box(3,13,26,4,gold);c.Box(8,8,16,14,gold);c.Box(12,11,9,9,new Color(1,.91f,.46f));break;
                case "shield":
                    c.Box(6,4,20,19,ink);c.Box(9,23,14,4,ink);c.Box(13,27,6,3,ink);c.Box(8,6,16,15,steel);c.Box(11,21,10,4,steel);c.Box(14,24,4,3,steel);c.Box(11,8,10,11,soul);c.Box(14,18,4,5,soul);break;
                case "sword":
                    c.Line(9,23,25,7,ink,5);c.Line(10,22,25,7,light,3);c.Line(12,21,26,7,steel,2);c.Line(8,18,17,27,gold,3);c.Line(7,25,11,21,wood,4);c.Box(4,27,4,3,gold);break;
                case "speed":
                    c.Line(5,21,23,6,soul,4);c.Line(10,25,27,10,light,3);c.Line(4,13,14,13,gold,2);c.Line(2,19,10,19,gold,2);c.Box(20,5,8,6,light);break;
                case "range":
                    c.Box(14,3,4,26,steel);c.Box(3,14,26,4,steel);c.Box(10,10,12,12,ink);c.Box(13,13,6,6,soul);break;
                case "book":
                    c.Box(5,5,23,23,ink);c.Box(7,7,9,17,new Color(.76f,.67f,.43f));c.Box(17,7,9,17,new Color(.88f,.8f,.57f));c.Box(15,7,2,19,wood);c.Box(8,8,5,13,new Color(.2f,.53f,.46f));c.Box(19,10,5,2,wood);c.Box(19,15,5,2,wood);break;
                case "bag":
                    c.Box(5,10,22,18,ink);c.Box(10,5,12,7,ink);c.Box(12,7,8,3,gold);c.Box(7,12,18,14,wood);c.Box(7,13,18,3,gold);c.Box(14,14,4,7,gold);break;
                case "refresh":
                    c.Line(6,12,10,6,gold,3);c.Line(10,6,24,6,gold,3);c.Line(24,6,26,12,gold,3);c.Box(20,10,9,4,gold);c.Box(25,6,4,8,gold);
                    c.Line(25,20,21,26,soul,3);c.Line(21,26,7,26,soul,3);c.Line(7,26,5,20,soul,3);c.Box(3,18,9,4,soul);c.Box(3,18,4,8,soul);break;
                case "close":c.Line(7,7,23,23,light,4);c.Line(23,7,7,23,light,4);break;
                case "ready":c.Line(5,17,12,24,soul,4);c.Line(12,24,26,7,soul,4);break;
                case "skip":c.Line(6,7,17,16,light,3);c.Line(17,16,6,25,light,3);c.Box(24,7,3,21,gold);break;
                case "skull":c.Box(6,5,20,17,light);c.Box(10,21,12,7,light);c.Box(9,11,5,5,ink);c.Box(19,11,5,5,ink);c.Box(15,17,3,4,ink);c.Box(13,23,2,4,ink);c.Box(18,23,2,4,ink);break;
                default:c.Box(12,3,8,26,soul);c.Box(6,9,20,14,soul);c.Box(10,8,12,3,light);c.Box(14,5,3,10,light);break;
            }
        }
        // Normalize species aliases too; HUD callers normally supply the catalog's model.
        static string CreatureModel(string id)
        {
            switch(id)
            {
                case "allay":case "vex":return "fairy";
                case "axolotl":case "cod":case "dolphin":case "pufferfish":case "salmon":case "tadpole":case "tropicalfish":return "fish";
                case "bogged":case "parched":case "stray":case "witherskeleton":return "skeleton";
                case "camelhusk":return "camel";
                case "cavespider":return "spider";
                case "chicken":case "parrot":return "bird";
                case "coppergolem":case "irongolem":return "golem";
                case "cat":case "cow":case "donkey":case "fox":case "goat":case "hoglin":case "horse":case "llama":case "mooshroom":case "mule":case "ocelot":case "panda":case "pig":case "polarbear":case "ravager":case "sheep":case "skeletonhorse":case "traderllama":case "wolf":case "zoglin":case "zombiehorse":return "quadruped";
                case "drowned":case "giant":case "husk":case "zombievillager":case "zombifiedpiglin":return "zombie";
                case "elderguardian":return "guardian";
                case "enderdragon":return "dragon";
                case "endermite":case "silverfish":return "arthropod";
                case "evoker":case "illusioner":case "pillager":case "vindicator":return "illager";
                case "glowsquid":return "squid";
                case "happyghast":return "ghast";
                case "magma":case "magmacube":return "slime";
                case "piglinbrute":return "piglin";
                case "villager":case "wanderingtrader":case "witch":return "humanoid";
                case "zombienautilus":return "nautilus";
                default:return id;
            }
        }
        static void PaintCreature(Canvas c,string model,Color skin)
        {
            Color shade=Color.Lerp(skin,Color.black,.3f),shine=Color.Lerp(skin,light,.4f),glow=new Color(.49f,1,.85f);
            c.Box(6,29,20,2,new Color(0,0,0,.32f));
            // Each catalog model has an explicit silhouette; aquatic and four-legged mobs never fall through to a person.
            switch(model)
            {
                case "slime":
                    c.Box(4,6,24,22,shade);c.Box(6,8,20,17,skin);c.Box(7,9,7,3,shine);
                    c.Box(10,16,4,4,ink);c.Box(21,16,4,4,ink);c.Box(16,23,4,2,ink);return;
                case "spider":
                    for(int i=0;i<4;i++){c.Line(1,7+i*6,11,13+i*2,shade,2);c.Line(20,13+i*2,29,7+i*6,shade,2);}
                    c.Box(10,8,13,15,skin);c.Box(9,19,15,9,shade);c.Box(10,20,3,3,Color.red);c.Box(20,20,3,3,Color.red);
                    c.Box(14,21,2,2,Color.red);c.Box(17,21,2,2,Color.red);return;
                case "arthropod":
                    c.Box(3,17,7,8,shade);c.Box(8,13,7,11,skin);c.Box(14,12,7,12,shade);c.Box(20,15,7,11,skin);
                    for(int i=0;i<4;i++){c.Line(6+i*6,22,3+i*6,28,shade,2);c.Box(8+i*5,12,2,3,shine);}
                    c.Line(25,16,29,11,shade,1);c.Line(25,19,30,18,shade,1);c.Box(23,17,2,2,ink);return;
                case "fish":
                    c.Box(6,11,16,12,skin);c.Box(9,8,10,3,shade);c.Box(9,23,9,3,shade);
                    c.Box(2,13,6,8,shine);c.Box(23,13,4,8,shade);c.Box(27,9,4,16,skin);
                    c.Box(7,14,3,3,ink);c.Box(2,19,3,2,shade);c.Box(15,16,5,5,shine);return;
                case "turtle":
                    c.Box(8,8,17,4,shade);c.Box(5,12,23,11,shade);c.Box(8,11,17,11,skin);
                    c.Box(12,10,8,5,shine);c.Box(10,17,12,5,shine);c.Line(15,10,15,22,shade,1);
                    c.Box(1,14,7,7,skin);c.Box(2,15,2,2,ink);c.Box(7,22,6,5,skin);c.Box(21,22,6,5,skin);
                    c.Box(8,6,5,4,skin);c.Box(22,7,5,5,skin);c.Box(28,17,3,2,shade);return;
                case "frog":
                    c.Box(6,12,20,12,skin);c.Box(5,7,7,8,skin);c.Box(20,7,7,8,skin);
                    c.Box(7,8,4,4,light);c.Box(21,8,4,4,light);c.Box(8,9,2,3,ink);c.Box(22,9,2,3,ink);
                    c.Box(3,20,8,7,shade);c.Box(22,20,7,7,shade);c.Box(7,26,7,3,skin);c.Box(19,26,8,3,skin);
                    c.Box(10,18,13,2,shade);c.Box(12,22,9,3,shine);return;
                case "guardian":
                    c.Box(7,8,18,18,skin);c.Box(10,5,3,5,shine);c.Box(20,4,3,5,shine);
                    c.Box(3,11,5,3,shine);c.Box(24,11,5,3,shine);c.Box(3,22,5,3,shine);c.Box(25,21,5,3,shine);
                    c.Box(10,26,3,4,shine);c.Box(20,25,3,5,shine);c.Box(11,13,10,9,light);c.Box(15,14,5,6,ink);
                    c.Box(16,15,3,4,new Color(.83f,.29f,.25f));c.Box(26,16,4,5,shade);return;
                case "nautilus":
                    c.Box(4,7,18,20,shade);c.Box(7,5,12,23,skin);c.Box(6,10,14,13,shine);
                    c.Box(10,10,9,3,shade);c.Box(8,12,3,9,shade);c.Box(10,20,7,3,shade);c.Box(15,14,3,8,shade);
                    c.Box(22,15,7,8,skin);c.Box(24,16,2,2,ink);c.Line(27,20,30,24,shine,2);
                    c.Line(24,22,28,28,shine,2);c.Line(22,22,22,28,skin,2);return;
                case "squid":
                    c.Box(7,3,18,17,shade);c.Box(9,4,14,14,skin);c.Box(10,12,4,4,light);c.Box(20,12,4,4,light);
                    c.Box(11,13,2,3,ink);c.Box(21,13,2,3,ink);
                    for(int i=0;i<4;i++){int x=6+i*6;c.Box(x,19,3,6,skin);c.Box(x+(i<2?-2:2),24,3,5,shade);}return;
                case "ghast":
                    c.Box(5,3,22,20,shade);c.Box(7,4,18,17,skin);c.Box(10,12,4,3,ink);c.Box(20,12,4,3,ink);
                    c.Box(11,15,2,4,shade);c.Box(21,15,2,4,shade);c.Box(15,17,3,3,ink);
                    for(int i=0;i<4;i++)c.Box(6+i*6,22,3,5+i%2*3,skin);return;
                case "blaze":
                    c.Box(10,5,13,12,skin);c.Box(12,9,3,3,ink);c.Box(20,9,2,3,ink);c.Box(15,14,5,2,new Color(1,.85f,.24f));
                    c.Box(3,11,4,11,skin);c.Box(25,10,4,11,skin);c.Box(7,19,4,10,shine);c.Box(21,20,4,9,shine);
                    c.Box(13,20,6,7,shade);c.Box(4,8,2,3,shine);c.Box(26,6,2,3,shine);return;
                case "breeze":
                    c.Box(10,3,13,12,skin);c.Box(8,5,17,3,shine);c.Box(11,9,3,2,ink);c.Box(19,9,3,2,ink);
                    c.Box(4,16,24,4,shade);c.Box(7,17,20,2,shine);c.Box(7,22,19,3,skin);c.Box(11,27,11,3,shine);
                    c.Box(1,13,7,2,shine);c.Box(25,21,6,2,shine);return;
                case "bat":
                    // Membrane wings, pointed ears and a small dark muzzle; bats have no bird beak.
                    c.Box(1,7,3,13,shade);c.Box(4,10,4,12,skin);c.Box(8,14,5,11,shade);
                    c.Box(28,7,3,13,shade);c.Box(24,10,4,12,skin);c.Box(19,14,5,11,shade);
                    c.Line(2,7,13,18,shine,1);c.Line(29,7,18,18,shine,1);
                    c.Box(12,7,3,6,skin);c.Box(18,7,3,6,skin);c.Box(12,12,9,10,skin);c.Box(14,21,5,7,shade);
                    c.Box(13,15,2,2,light);c.Box(18,15,2,2,light);c.Box(15,19,3,2,ink);return;
                case "bee":
                    c.Box(6,6,8,6,new Color(.81f,.95f,1,.85f));c.Box(18,6,8,6,new Color(.81f,.95f,1,.85f));
                    c.Box(6,13,21,11,skin);c.Box(12,13,3,11,ink);c.Box(20,13,3,11,ink);
                    c.Box(5,16,2,3,ink);c.Box(27,17,3,3,ink);c.Box(7,11,2,3,ink);c.Box(10,10,2,4,ink);
                    c.Box(9,24,2,4,ink);c.Box(16,24,2,4,ink);c.Box(23,24,2,4,ink);return;
                case "bird":
                    c.Box(9,12,14,13,skin);c.Box(12,5,11,12,skin);c.Box(21,11,7,4,gold);c.Box(18,8,2,3,ink);
                    c.Box(6,15,7,7,shade);c.Box(13,18,7,6,shine);c.Box(3,13,5,4,shade);
                    c.Box(12,25,2,5,gold);c.Box(20,25,2,5,gold);c.Box(10,29,5,2,gold);c.Box(18,29,5,2,gold);return;
                case "fairy":
                    c.Box(3,11,7,10,shine);c.Box(23,11,6,10,shine);c.Box(6,8,5,6,shine);c.Box(21,8,5,6,shine);
                    c.Box(11,4,11,10,skin);c.Box(13,8,2,2,light);c.Box(19,8,2,2,light);c.Box(13,15,7,9,skin);
                    c.Line(11,16,8,23,skin,2);c.Line(21,16,24,23,skin,2);c.Box(13,24,2,6,shade);c.Box(18,24,2,6,shade);return;
                case "dragon":
                    c.Line(1,4,12,17,shade,4);c.Line(29,4,19,17,shade,4);
                    c.Box(2,7,5,9,skin);c.Box(6,11,5,9,skin);c.Box(24,7,5,9,skin);c.Box(20,11,5,9,skin);
                    c.Box(12,12,8,15,shade);c.Box(11,5,10,10,skin);c.Box(10,2,3,5,shine);c.Box(20,2,3,5,shine);
                    c.Box(12,8,2,2,new Color(.8f,.4f,1));c.Box(19,8,2,2,new Color(.8f,.4f,1));c.Box(12,27,4,4,skin);return;
                case "phantom":
                    c.Box(1,9,8,5,skin);c.Box(7,12,7,6,shade);c.Box(23,9,8,5,skin);c.Box(18,12,7,6,shade);
                    c.Box(11,12,10,9,skin);c.Box(11,9,10,5,shade);c.Box(12,11,3,2,glow);c.Box(18,11,3,2,glow);
                    c.Box(14,20,4,6,shade);c.Box(15,26,2,4,skin);return;
                case "quadruped":
                    c.Box(10,12,18,12,skin);c.Box(2,8,12,13,skin);c.Box(3,5,3,5,shade);c.Box(10,5,3,5,shade);
                    c.Box(2,17,8,5,shine);c.Box(4,12,2,3,ink);c.Box(11,13,2,2,ink);c.Box(14,14,11,3,shine);
                    c.Box(5,22,4,8,shade);c.Box(11,22,3,7,skin);c.Box(20,23,3,6,skin);c.Box(25,22,4,8,shade);
                    c.Line(27,14,30,9,shade,2);return;
                case "camel":
                    c.Box(8,13,19,10,skin);c.Box(14,8,8,6,shade);c.Box(3,8,5,14,skin);c.Box(1,5,11,6,skin);
                    c.Box(3,3,2,3,shade);c.Box(6,7,2,2,ink);c.Box(9,22,3,9,shade);c.Box(14,22,2,8,skin);
                    c.Box(22,22,2,8,skin);c.Box(26,21,3,10,shade);c.Line(28,14,30,20,shade,1);return;
                case "rabbit":
                    c.Box(7,2,3,12,skin);c.Box(14,1,3,12,skin);c.Box(8,3,1,8,shine);c.Box(15,2,1,8,shine);
                    c.Box(5,12,14,11,skin);c.Box(8,15,3,3,ink);c.Box(16,16,2,2,ink);c.Box(5,20,6,3,shine);
                    c.Box(14,18,12,10,skin);c.Box(20,23,8,7,shade);c.Box(6,27,8,3,skin);c.Box(27,19,4,5,light);return;
                case "armadillo":
                    c.Box(8,10,17,15,shade);c.Box(11,7,11,5,shade);c.Box(10,10,13,12,skin);
                    c.Line(13,9,13,23,shine,1);c.Line(18,9,18,23,shine,1);c.Box(2,17,9,7,skin);c.Box(4,18,2,2,ink);
                    c.Box(6,12,2,6,shade);c.Box(9,24,3,5,skin);c.Box(22,24,3,5,skin);c.Line(26,21,30,24,skin,2);return;
                case "sniffer":
                    c.Box(5,12,23,13,shade);c.Box(8,6,18,9,skin);c.Box(5,10,24,8,skin);c.Box(11,7,10,3,shine);
                    c.Box(1,17,12,9,new Color(.65f,.3f,.22f));c.Box(1,20,10,5,new Color(.85f,.68f,.28f));c.Box(10,17,2,2,ink);
                    c.Box(7,25,4,5,shade);c.Box(13,25,3,4,skin);c.Box(22,25,3,4,skin);c.Box(26,24,4,6,shade);return;
                case "strider":
                    c.Box(6,5,21,17,shade);c.Box(8,6,17,14,skin);c.Box(10,13,3,3,ink);c.Box(20,13,3,3,ink);
                    c.Box(3,10,4,2,light);c.Box(1,15,6,2,light);c.Box(26,10,4,2,light);c.Box(26,15,6,2,light);
                    c.Box(10,21,3,10,shade);c.Box(21,21,3,10,shade);c.Box(14,18,5,2,shine);return;
                case "shulker":
                    c.Box(4,4,24,9,shade);c.Box(6,5,20,6,skin);c.Box(3,22,26,8,shade);c.Box(5,23,22,5,skin);
                    c.Box(9,12,14,11,shine);c.Box(11,15,3,3,ink);c.Box(19,15,3,3,ink);c.Box(7,7,18,2,shine);return;
                case "creeper":
                    c.Box(8,3,17,14,skin);c.Box(10,8,4,4,ink);c.Box(19,8,4,4,ink);c.Box(14,11,5,5,ink);
                    c.Box(11,14,4,4,ink);c.Box(19,14,4,4,ink);c.Box(11,17,11,8,shade);
                    c.Box(7,25,7,6,skin);c.Box(19,25,7,6,skin);c.Box(13,26,3,4,shade);c.Box(17,26,3,4,shade);return;
                case "enderman":
                    c.Box(10,2,13,10,skin);c.Box(11,7,4,2,new Color(.87f,.45f,1));c.Box(18,7,4,2,new Color(.87f,.45f,1));
                    c.Box(12,12,9,9,shade);c.Box(7,12,3,17,skin);c.Box(23,12,3,17,skin);c.Box(12,21,3,11,skin);c.Box(18,21,3,11,skin);return;
                case "golem":
                    c.Box(10,2,13,9,skin);c.Box(12,6,3,2,ink);c.Box(20,6,2,2,ink);c.Box(15,8,4,4,shade);
                    c.Box(7,11,20,14,skin);c.Box(2,12,5,16,shade);c.Box(27,12,5,16,shade);c.Box(10,25,5,7,shade);c.Box(21,25,5,7,shade);
                    c.Box(12,13,2,9,new Color(.24f,.5f,.25f));c.Box(13,19,4,2,new Color(.24f,.5f,.25f));return;
                case "snowgolem":
                    c.Box(9,2,15,12,new Color(.91f,.46f,.12f));c.Box(11,6,3,3,ink);c.Box(20,6,3,3,ink);c.Box(14,11,5,2,ink);
                    c.Box(10,14,13,8,light);c.Box(7,22,19,9,light);c.Box(15,17,2,2,ink);c.Box(15,25,2,2,ink);
                    c.Line(9,17,1,11,new Color(.48f,.28f,.14f),2);c.Line(23,17,30,11,new Color(.48f,.28f,.14f),2);return;
                case "warden":
                    c.Box(7,6,19,10,skin);c.Box(3,3,4,11,shade);c.Box(26,3,4,11,shade);c.Box(1,2,5,4,glow);c.Box(27,2,5,4,glow);
                    c.Box(8,16,18,10,shade);c.Box(2,15,5,14,skin);c.Box(27,15,5,14,skin);c.Box(9,26,5,6,skin);c.Box(21,26,5,6,skin);
                    c.Box(12,18,10,6,glow);c.Box(15,17,4,8,shine);c.Box(12,10,10,3,ink);return;
                case "wither":
                    c.Box(11,15,10,12,shade);c.Box(1,7,9,10,skin);c.Box(12,3,9,12,skin);c.Box(23,7,8,10,skin);
                    c.Box(3,11,2,2,light);c.Box(7,11,2,2,light);c.Box(14,7,2,2,light);c.Box(18,7,2,2,light);c.Box(25,11,2,2,light);c.Box(29,11,2,2,light);
                    c.Box(11,19,11,2,light);c.Box(13,24,7,2,light);c.Box(14,27,4,4,shade);return;
                case "creaking":
                    c.Box(11,4,10,12,skin);c.Box(12,8,2,2,gold);c.Box(18,8,2,2,gold);c.Box(15,12,2,2,gold);
                    c.Box(13,15,7,9,shade);c.Line(13,16,4,11,skin,3);c.Line(20,17,28,9,skin,3);c.Line(4,11,1,6,shade,2);
                    c.Line(27,10,29,3,shade,2);c.Box(11,24,3,8,skin);c.Box(19,23,3,9,skin);c.Box(14,1,3,5,shade);return;
                case "piglin":
                    c.Box(8,4,18,12,skin);c.Box(4,6,5,5,shade);c.Box(25,6,5,5,shade);c.Box(11,8,3,2,ink);c.Box(21,8,3,2,ink);
                    c.Box(13,10,9,6,shine);c.Box(14,12,2,2,shade);c.Box(19,12,2,2,shade);c.Box(11,14,2,4,light);c.Box(23,14,2,4,light);
                    c.Box(11,17,13,9,shade);c.Box(6,17,4,10,skin);c.Box(25,17,4,10,skin);c.Box(11,26,4,6,shade);c.Box(21,26,4,6,shade);c.Box(11,22,13,2,gold);return;
                case "skeleton":
                    c.Box(9,3,15,11,skin);c.Box(11,7,4,4,ink);c.Box(19,7,4,4,ink);c.Box(15,11,3,3,ink);
                    c.Box(15,14,3,11,skin);c.Box(11,17,11,2,shine);c.Box(11,21,11,2,shine);c.Line(11,15,6,25,skin,2);
                    c.Line(22,15,27,24,skin,2);c.Box(11,25,3,7,skin);c.Box(21,25,3,7,skin);return;
                case "illager":
                case "humanoid":
                    c.Box(9,3,15,12,skin);c.Box(11,7,3,2,ink);c.Box(20,7,3,2,ink);c.Box(16,8,4,9,shade);
                    c.Box(10,16,14,15,shade);c.Box(6,17,4,9,skin);c.Box(24,17,4,9,skin);c.Box(9,22,16,4,skin);
                    c.Box(12,29,4,3,ink);c.Box(20,29,4,3,ink);
                    if(model=="illager"){c.Box(10,5,13,2,shade);c.Box(8,25,18,2,new Color(.35f,.21f,.12f));}return;
                case "zombie":
                default:
                    c.Box(9,3,15,13,skin);c.Box(11,8,3,3,ink);c.Box(20,8,3,3,ink);c.Box(14,13,7,2,shade);
                    c.Box(11,16,11,9,new Color(.19f,.57f,.56f));c.Box(5,16,5,10,skin);c.Box(23,16,5,10,skin);
                    c.Box(11,25,4,7,shade);c.Box(18,25,4,7,shade);return;
            }
        }
    }
}
