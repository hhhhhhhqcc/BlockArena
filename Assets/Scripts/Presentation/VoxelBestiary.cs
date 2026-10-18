using System;
using System.Collections.Generic;
using UnityEngine;
using BlockArena.Core;

namespace BlockArena.Presentation
{
    // Species share articulated archetypes, but each species bakes its own silhouette,
    // markings and equipment into reusable meshes. No textures or external assets.
    public sealed partial class VoxelCreature
    {
        string modelFamily = "zombie", modelColor = "#8CAE74";
        float modelHover, modelFootprint = .57f;
        bool modelFlying;
        readonly List<Transform> wings = new List<Transform>();
        readonly List<Transform> tails = new List<Transform>();
        readonly List<Transform> orbitParts = new List<Transform>();
        readonly List<Transform> earParts = new List<Transform>();
        Transform tongue;

        void ConfigureBestiary()
        {
            if (Kind == "steve") { modelFamily = "steve"; return; }
            CreatureDefinition definition = CreatureCatalog.Get(Kind);
            modelFamily = string.IsNullOrEmpty(definition.model) ? Kind : definition.model;
            modelColor = string.IsNullOrEmpty(definition.color) ? "#8CAE74" : definition.color;
            modelFlying = definition.flying;
            modelHover = modelFlying ? .65f : 0;
            if (modelFamily == "fish" || modelFamily == "squid" || modelFamily == "nautilus") modelHover = .36f;
            if (modelFamily == "dragon") modelHover = 1.05f;
            if (modelFamily == "ghast" || modelFamily == "wither") modelHover = .9f;
            if (modelFamily == "spider") modelFootprint = .76f;
            if (modelFamily == "golem") modelFootprint = .72f;
            if (modelFamily == "slime") modelFootprint = .64f;
        }

        void ApplyNativePalette(VoxelMesh mesh)
        {
            if (Kind == "husk" || Kind == "drowned" || Kind == "stray" || Kind == "bogged" || Kind == "parched" || Kind == "wither_skeleton" || Kind == "cave_spider" || Kind == "magma_cube")
                mesh.MapColors(RemapNativeColor);
        }
        Color RemapNativeColor(Color color)
        {
            float l = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (Kind == "magma_cube") return Color.Lerp(VoxelAssets.Hex("#241E27"), VoxelAssets.Hex("#D25927"), l * .82f);
            if (Kind == "wither_skeleton") return Color.Lerp(VoxelAssets.Hex("#141C24"), VoxelAssets.Hex("#52565A"), l);
            if (Kind == "cave_spider") return color.r > color.g * 1.6f && color.r > .5f ? color : Color.Lerp(VoxelAssets.Hex("#1F3742"), VoxelAssets.Hex("#436D6C"), l);
            if (Kind == "stray") return Color.Lerp(color, new Color(l * .75f, l * .86f, l), .5f);
            if (Kind == "bogged") return Color.Lerp(color, new Color(l * .72f, l * .83f, l * .56f), .58f);
            if (Kind == "parched") return Color.Lerp(color, new Color(l, l * .77f, l * .48f), .58f);
            bool green = color.g > color.r * 1.03f && color.g > color.b * 1.08f;
            if (Kind == "drowned") return green ? new Color(l * .4f, l * .87f, l * .82f) : Color.Lerp(color, new Color(l * .65f, l * .72f, l * .62f), .35f);
            if (Kind == "husk") return green ? new Color(l, l * .78f, l * .48f) : Color.Lerp(color, new Color(l * .82f, l * .73f, l * .51f), .65f);
            return color;
        }
        void VariantDetails()
        {
            if (Kind == "stray" || Kind == "parched") Part("Weathered hood", Vector3.zero, m => {
                string cloth = Kind == "stray" ? "#778D95" : "#A68350";
                B(m, 0, .18f, .04f, .61f, .22f, .59f, cloth); B(m, -.3f, -.07f, .025f, .07f, .4f, .58f, cloth); B(m, .3f, -.07f, .025f, .07f, .4f, .58f, cloth);
                B(m, 0, -.27f, -.08f, .51f, .12f, .42f, cloth);
            }, head);
            if (Kind == "bogged") { Mushroom(head, new Vector3(-.16f, .35f, .05f), .5f, "#A66B4E"); Mushroom(head, new Vector3(.19f, .31f, .08f), .35f, "#B47756"); }
            if (Kind == "magma_cube") Part("Molten seams", Vector3.zero, m => {
                for (int i = 0; i < 4; i++) B(m, 0, .2f + i * .2f, -.55f, 1.07f, .035f, .02f, "#FFB13F");
                B(m, -.22f, .6f, -.58f, .18f, .17f, .025f, "#FFE17C"); B(m, .22f, .6f, -.58f, .18f, .17f, .025f, "#FFE17C");
            });
            if (Kind == "wither_skeleton")
            {
                if (bow != null) bow.gameObject.SetActive(false);
                Part("Stone sword", new Vector3(0, -.53f, -.09f), m => Weapon(m, "sword", "#8D969C"), arms[1]);
            }
        }
        static void Eyes(VoxelMesh m, float x, float y, float z, float size, string color = "#202B29")
        { B(m, -x, y, z, size, size, .025f, color); B(m, x, y, z, size, size, .025f, color); }
        void Mushroom(Transform parent, Vector3 at, float scale, string cap)
        {
            Transform t = Part("Mushroom " + at, at, m => { B(m, 0, .14f, 0, .09f, .27f, .09f, "#E2D1B5"); B(m, 0, .32f, 0, .45f, .15f, .42f, cap); B(m, -.1f, .403f, .05f, .12f, .018f, .11f, "#F4E7CA"); B(m, .13f, .403f, -.1f, .08f, .018f, .09f, "#F4E7CA"); }, parent);
            t.localScale = Vector3.one * scale;
        }
        static void Weapon(VoxelMesh m, string kind, string color)
        {
            B(m, 0, -.06f, 0, .07f, .3f, .07f, "#725038");
            if (kind == "axe") { B(m, .12f, .24f, 0, .29f, .22f, .07f, color); B(m, 0, .12f, 0, .055f, .5f, .055f, "#93704A"); }
            else if (kind == "crossbow") { B(m, 0, 0, -.12f, .12f, .12f, .55f, "#73573D"); B(m, 0, 0, -.28f, .59f, .07f, .07f, "#B18C58"); B(m, 0, .05f, -.33f, .04f, .035f, .64f, color); }
            else { B(m, 0, .15f, 0, .32f, .07f, .08f, "#6C7779"); B(m, 0, .5f, 0, .11f, .64f, .05f, color); B(m, -.03f, .84f, 0, .06f, .06f, .05f, color); }
        }

        void BuildBestiary()
        {
            if (Kind == "copper_golem") { BuildCopperGolem(); return; }
            if (Kind == "zombified_piglin") { BuildPiglin(true); return; }
            if (Kind == "zombie_villager") { BuildVillager(true); return; }
            switch (modelFamily)
            {
                case "quadruped": BuildQuadruped(); break;
                case "camel": BuildCamel(); break;
                case "bird": BuildBird(); break;
                case "fish": BuildFish(); break;
                case "dragon": BuildDragon(); break;
                case "ghast": BuildGhast(); break;
                case "blaze": case "breeze": BuildElemental(); break;
                case "warden": BuildWarden(); break;
                case "piglin": BuildPiglin(false); break;
                case "illager": case "humanoid": BuildVillager(false); break;
                case "guardian": BuildGuardian(); break;
                case "arthropod": BuildArthropod(); break;
                case "turtle": BuildTurtle(); break;
                case "nautilus": BuildNautilus(); break;
                case "enderman": BuildEnderman(); break;
                case "shulker": BuildShulker(); break;
                case "bat": case "phantom": BuildWinged(); break;
                case "frog": BuildFrog(); break;
                case "squid": BuildSquid(); break;
                case "bee": BuildBee(); break;
                case "rabbit": BuildRabbit(); break;
                case "snow_golem": BuildSnowGolem(); break;
                case "wither": BuildWither(); break;
                case "fairy": BuildFairy(); break;
                case "strider": BuildStrider(); break;
                case "creaking": BuildCreaking(); break;
                case "armadillo": BuildArmadillo(); break;
                case "sniffer": BuildSniffer(); break;
                default: BuildQuadruped(); break;
            }
        }

        void BuildQuadruped()
        {
            bool cat = Kind == "cat" || Kind == "ocelot", canine = Kind == "wolf" || Kind == "fox";
            bool horse = Kind.Contains("horse") || Kind == "donkey" || Kind == "mule";
            bool llama = Kind == "llama" || Kind == "trader_llama";
            bool bear = Kind == "panda" || Kind == "polar_bear";
            bool hog = Kind == "hoglin" || Kind == "zoglin", ravager = Kind == "ravager";
            bool cow = Kind == "cow" || Kind == "mooshroom", goat = Kind == "goat", sheep = Kind == "sheep";
            float leg = horse ? .9f : llama ? .82f : ravager ? .7f : cat ? .32f : canine ? .45f : .5f;
            float width = ravager ? 1.4f : bear ? 1.02f : hog ? 1.12f : cat ? .38f : canine ? .48f : .79f;
            float length = horse ? 1.55f : llama ? 1.2f : ravager ? 1.65f : hog ? 1.55f : bear ? 1.5f : cat ? .94f : 1.16f;
            float thickness = ravager ? .85f : bear ? .72f : cat ? .38f : canine ? .49f : .65f;
            float torsoY = leg + thickness * .5f;
            string skin = Kind == "pig" ? "#D99392" : cow ? (Kind == "mooshroom" ? "#AF493C" : "#58483B") : sheep ? "#E8E3CC" : goat ? "#E8DDC5" : Kind == "wolf" ? "#B6B6A8" : Kind == "fox" ? "#CC793F" : Kind == "cat" ? "#566365" : Kind == "ocelot" ? "#D3AD55" : Kind == "panda" ? "#DDDCCA" : Kind == "polar_bear" ? "#E9E3CC" : Kind == "zoglin" ? "#9C746D" : hog ? "#A46E57" : ravager ? "#6B7470" : Kind == "skeleton_horse" ? "#C9C8B1" : Kind == "zombie_horse" ? "#60866A" : Kind == "donkey" ? "#9A9588" : Kind == "mule" ? "#736356" : llama ? "#CEB996" : "#8B5F40";
            Height = leg + thickness + (horse || llama ? .85f : ravager ? .35f : .3f);
            modelFootprint = Mathf.Max(.45f, width * .62f);
            body = Part("Animal body", Vector3.zero, m => {
                B(m, 0, torsoY, 0, width, thickness, length, skin);
                if (Kind == "skeleton_horse") { B(m, 0, torsoY, -.1f, width + .025f, .14f, length * .7f, "#8D9889"); for(int r=0;r<4;r++)B(m,0,torsoY+.04f,-.44f+r*.28f,width+.04f,thickness+.025f,.09f,"#D4D4BD"); }
                else if (cow || Kind == "ocelot") Mottle(m,new Vector3(0,torsoY,0),new Vector3(width,thickness,length),cow?5:8,4,Kind=="mooshroom"?19:7,cow?"#D8D4BF":"#F0CE78",cow?"#9F9275":"#6D633D");
                else if (sheep) { B(m,0,torsoY+.1f,0,width+.12f,thickness+.15f,length+.06f,"#EBE6D0"); B(m,-width*.5f-.04f,torsoY-.03f,.12f,.12f,.39f,.67f,"#D1D2BE"); }
                else if (Kind == "panda") B(m,0,torsoY,-length*.27f,width+.025f,thickness+.025f,.36f,"#333E3C");
                else if (cat || canine || goat || Kind=="polar_bear") B(m,0,torsoY-thickness*.42f,-.1f,width*.8f,.13f,length*.8f,Kind=="wolf"?"#DCDCCA":"#EEE5CC");
                if (hog) for(int i=0;i<5;i++)B(m,0,torsoY+thickness*.5f+.08f,-.45f+i*.22f,.12f,.19f,.14f,"#594A3D");
                if (ravager) { B(m,0,torsoY+.1f,0,width+.05f,.19f,.35f,"#454E4E"); B(m,-width*.53f,torsoY+.1f,0,.08f,.23f,.24f,"#A4A68B"); B(m,width*.53f,torsoY+.1f,0,.08f,.23f,.24f,"#A4A68B"); }
                if (Kind=="trader_llama") { B(m,0,torsoY+thickness*.5f+.026f,0,width+.08f,.05f,.72f,"#2E71A5"); for(int side=-1;side<=1;side+=2){B(m,side*(width*.5f+.045f),torsoY+.03f,0,.06f,.57f,.72f,"#337CB1");B(m,side*(width*.5f+.08f),torsoY-.2f,0,.025f,.1f,.74f,"#E8C263");} }
            });
            float hy = torsoY + (horse ? .61f : llama ? .81f : .1f), hz = -length*.5f - (horse || llama ? .06f : .2f);
            head = Part("Animal head",new Vector3(0,hy,hz),m=>{
                float hw=ravager?1.05f:bear? 0.77f:cat? 0.48f:canine? 0.52f:horse? 0.42f:llama? 0.4f: 0.6f;
                float hh=ravager? 0.83f:horse? 0.64f:llama? 0.58f: 0.48f, hd=ravager? 0.75f:bear? 0.55f:horse? 0.7f: 0.5f;
                if(horse||llama){B(m,0,-.35f,.12f,hw*.8f,.8f,.42f,skin);B(m,0,.03f,0,hw,hh,hd,skin);}
                else B(m,0,0,0,hw,hh,hd,skin);
                string muzzle=Kind=="pig"?"#E6AB9D":cow?"#C6A095":cat||canine?"#E9DDC3":ravager?"#8C938A":skin;
                float mz=-hd*.5f-.1f;
                B(m,0,-hh*.19f,mz,hw*(canine? 0.7f: 0.74f),hh*.4f,.25f,muzzle);
                if(Kind=="pig"||hog){B(m,0,-.06f,-hd*.5f-.17f,hw*.72f,.23f,.19f,Kind=="pig"?"#C6787C":"#BF9079");Eyes(m,hw*.17f,-.06f,-hd*.5f-.275f,.055f,"#764B4B");}
                else if(cat||canine||bear)B(m,0,-.05f,mz-.14f,hw*.25f,.09f,.045f,"#303937");
                Eyes(m,hw*.31f,hh*.12f,-hd*.5f-.018f,cat? 0.075f: 0.085f,cat?"#CFE48D":"#252E2B");
                if(Kind=="panda")for(int side=-1;side<=1;side+=2){B(m,side*hw*.29f,.07f,-hd*.5f-.027f,.18f,.21f,.018f,"#34413F");B(m,side*hw*.29f,.08f,-hd*.5f-.04f,.055f,.05f,.018f,"#DFE3D0");}
                for(int side=-1;side<=1;side+=2){
                    float eh=(Kind=="donkey"||Kind=="mule"||llama)? 0.37f:cat||canine? 0.22f: 0.15f;
                    B(m,side*hw*.4f,hh*.5f+eh*.35f,.02f,cat||canine? 0.17f: 0.14f,eh,.13f,skin);
                    if(cat||canine||horse||llama)B(m,side*hw*.4f,hh*.5f+eh*.35f,-.053f,.07f,eh*.65f,.018f,Kind=="fox"?"#443E36":"#BFA095");
                    if(cow||goat||ravager){float horn=ravager? 0.48f:goat? 0.38f: 0.2f;B(m,side*hw*.43f,hh*.5f+horn*.35f,.13f,.12f,horn,.15f,"#B9B69B");B(m,side*hw*.43f,hh*.5f+horn*.8f,.22f,.1f,.11f,.3f,goat?"#6F7161":"#D8D1B2");}
                    if(hog){B(m,side*hw*.42f,-.2f,-hd*.5f-.15f,.12f,.35f,.12f,"#DCD1AC");B(m,side*hw*.42f,-.06f,-hd*.5f-.2f,.1f,.1f,.23f,"#EEE2BC");}
                }
                if(goat)B(m,0,-.37f,-.19f,.15f,.27f,.16f,"#C6C5B0");
                if(ravager)B(m,0,-.1f,-hd*.5f-.2f,.22f,.44f,.28f,"#717D73");
                if(horse){B(m,0,.1f,hd*.5f+.015f,.12f,.72f,.12f,Kind=="skeleton_horse"?"#8A9686":"#4A3E34");if(Kind=="horse")B(m,0,.13f,-hd*.5f-.025f,.1f,.32f,.025f,"#E2D5B8");}
            });
            int count=4;
            for(int i=0;i<count;i++){int side=i%2==0?-1:1;float z=i<2?-length*.33f:length*.33f;
                legs.Add(Part("Animal leg "+i,new Vector3(side*width*.34f,leg,z),m=>{float lw=cat? 0.1f:horse? 0.16f:ravager? 0.31f: 0.19f;B(m,0,-leg*.48f,0,lw,leg,lw*1.16f,Kind=="panda"?"#34403D":skin);B(m,0,-leg+.065f,-.025f,lw+.03f,.13f,lw*1.35f,cat||canine?"#D6D5BD":"#4C5046");}));}
            tails.Add(Part("Animal tail",new Vector3(0,torsoY+.05f,length*.5f),m=>{
                if(Kind=="pig"){B(m,0,.02f,.13f,.08f,.08f,.3f,"#E7AC9C");B(m,.06f,.08f,.23f,.12f,.14f,.07f,"#D8978B");}
                else if(Kind=="fox"){B(m,0,.08f,.37f,.31f,.3f,.75f,"#B96A38");B(m,0,.1f,.75f,.3f,.29f,.22f,"#EFE5CA");}
                else if(cat){B(m,0,.3f,.19f,.09f,.6f,.11f,skin);B(m,0,.61f,.12f,.09f,.08f,.2f,skin);}
                else if(horse||cow||llama){B(m,0,-.2f,.13f,.11f,.58f,.13f,skin);B(m,0,-.45f,.15f,.18f,.22f,.19f,horse?"#3D3830":"#7E6E54");}
                else if(canine){RotatedBox(m,new Vector3(0,.15f,.28f),new Vector3(.17f,.17f,.65f),new Vector3(-25,0,0),skin);}
                else B(m,0,.03f,.11f,.17f,.17f,.22f,skin);
            }));
            if(Kind=="mooshroom"){Mushroom(figure,new Vector3(-.12f,torsoY+thickness*.5f,-.25f),.8f,"#C14636");Mushroom(figure,new Vector3(.12f,torsoY+thickness*.5f,.38f),.65f,"#BC4737");Mushroom(head,new Vector3(0,.27f,.05f),.5f,"#BE4A38");}
        }

        void BuildCamel()
        {
            bool undead=Kind=="camel_husk";string hide=undead?"#9F8B66":"#C7A363";Height=2.62f;modelFootprint=.75f;
            body=Part("Camel torso",Vector3.zero,m=>{B(m,0,1.55f,0,.88f,.72f,1.64f,hide);B(m,0,1.97f,.12f,.67f,.4f,.85f,undead?"#8B805F":"#B58C4D");B(m,0,2.18f,.12f,.45f,.11f,.57f,"#D7B57B");});
            head=Part("Long camel neck",new Vector3(0,2.15f,-.85f),m=>{B(m,0,-.35f,.1f,.35f,1.05f,.4f,hide);B(m,0,.18f,-.1f,.45f,.42f,.58f,hide);B(m,0,.06f,-.44f,.38f,.22f,.3f,"#E0C28E");Eyes(m,.16f,.23f,-.4f,.065f);for(int side=-1;side<=1;side+=2)B(m,side*.25f,.38f,.02f,.12f,.23f,.19f,hide);});
            for(int i=0;i<4;i++)legs.Add(Part("Camel leg "+i,new Vector3(i%2==0?-.3f: 0.3f,1.2f,i<2?-.57f: 0.58f),m=>{B(m,0,-.5f,0,.16f,1.15f,.18f,hide);B(m,0,-1.1f,-.06f,.23f,.14f,.31f,"#78684F");B(m,0,-.6f,-.02f,.2f,.16f,.23f,"#AD8D58");}));
            tails.Add(Part("Camel tail",new Vector3(0,1.7f,.85f),m=>{B(m,0,-.2f,.05f,.1f,.5f,.1f,hide);B(m,0,-.45f,.05f,.17f,.21f,.18f,"#745A38");}));
        }

        void BuildVillager(bool zombie)
        {
            bool witch=Kind=="witch",trader=Kind=="wandering_trader",illager=modelFamily=="illager";
            Height=witch?2.6f:2.02f;string skin=zombie?"#77945E":illager?"#A4ABA0":"#BD9A79";
            string robe=witch?"#63496E":trader?"#386B9A":Kind=="evoker"?"#34423F":Kind=="vindicator"?"#5A7580":Kind=="illusioner"?"#446C96":Kind=="pillager"?"#6A5B52":"#8B6952";
            body=Part("Robed torso",Vector3.zero,m=>{B(m,0,.98f,0,.62f,.82f,.39f,robe);B(m,0,.55f,0,.66f,.28f,.44f,robe);B(m,0,.87f,-.21f,.11f,.68f,.028f,trader||Kind=="evoker"?"#C6A663":"#57463D");if(trader)B(m,0,1.34f,0,.7f,.16f,.44f,"#D7BD6D");});
            head=Part("Villager face",new Vector3(0,1.66f,-.025f),m=>{B(m,0,0,0,.55f,.6f,.54f,skin);B(m,0,.075f,-.284f,.43f,.065f,.035f,"#594C3D");Eyes(m,.14f,-.005f,-.286f,.08f,illager?"#39664E":"#4D7557");B(m,0,-.115f,-.355f,.14f,.27f,.19f,skin);B(m,0,-.254f,-.294f,.2f,.035f,.03f,"#78624E");if(illager)B(m,0,.13f,.25f,.57f,.38f,.09f,"#6F756C");if(trader||Kind=="illusioner"){B(m,0,.265f,.015f,.62f,.17f,.61f,robe);B(m,-.29f,-.02f,.05f,.09f,.57f,.5f,robe);B(m,.29f,-.02f,.05f,.09f,.57f,.5f,robe);}if(witch){B(m,0,.32f,0,.92f,.11f,.85f,"#3B3A40");B(m,0,.52f,.015f,.59f,.31f,.57f,"#434444");B(m,-.06f,.77f,.02f,.37f,.23f,.36f,"#454544");B(m,-.13f,.95f,.02f,.2f,.16f,.2f,"#454544");B(m,0,.48f,-.285f,.6f,.105f,.025f,"#856C39");B(m,0,.49f,-.305f,.13f,.13f,.025f,"#9EC165");}});
            for(int i=0;i<2;i++){int side=i==0?-1:1;legs.Add(Part("Robe foot "+i,new Vector3(side*.16f,.54f,0),m=>{B(m,0,-.2f,0,.23f,.42f,.26f,"#5D5749");B(m,0,-.43f,-.06f,.25f,.13f,.34f,"#3C423B");}));arms.Add(Part("Robe arm "+i,new Vector3(side*.4f,1.26f,0),m=>{B(m,0,-.24f,0,.23f,.59f,.27f,robe);B(m,0,-.51f,0,.2f,.17f,.24f,skin);}));}
            if(Kind=="pillager")Part("Crossbow",new Vector3(0,-.5f,-.08f),m=>Weapon(m,"crossbow","#C6CBC0"),arms[1]);
            if(Kind=="vindicator")Part("Iron axe",new Vector3(0,-.5f,-.08f),m=>Weapon(m,"axe","#BFC9C2"),arms[1]);
            if(witch)Part("Potion bottle",new Vector3(0,-.55f,-.08f),m=>{B(m,0,0,0,.19f,.22f,.19f,"#AF8DC8");B(m,0,.14f,0,.09f,.1f,.09f,"#BBCBDD");B(m,0,.21f,0,.1f,.04f,.1f,"#B9A46E");},arms[1]);
        }

        void BuildPiglin(bool undead)
        {
            Height=1.97f;string skin=undead?"#B19E87":"#CE9A7F",cloth=Kind=="piglin_brute"?"#4E4944":"#775843";
            body=Part("Piglin jacket",Vector3.zero,m=>{B(m,0,1.02f,0,.57f,.67f,.35f,cloth);B(m,0,1.23f,-.19f,.24f,.21f,.025f,skin);B(m,0,.76f,-.195f,.58f,.1f,.035f,"#9C784D");if(Kind=="piglin_brute"){B(m,-.18f,1.23f,-.21f,.15f,.26f,.07f,"#DFC36C");B(m,.24f,.95f,-.2f,.08f,.5f,.025f,"#C8A55C");}});
            head=Part("Piglin ears and snout",new Vector3(0,1.59f,-.035f),m=>{B(m,0,0,0,.61f,.57f,.55f,skin);B(m,0,-.1f,-.36f,.34f,.22f,.25f,undead?"#8F9476":"#DDA98A");Eyes(m,.21f,.04f,-.29f,.075f);Eyes(m,.09f,-.08f,-.495f,.055f,"#8B6154");for(int side=-1;side<=1;side+=2){B(m,side*.37f,.03f,0,.2f,.26f,.13f,skin);B(m,side*.37f,.03f,-.075f,.11f,.17f,.018f,"#B37E72");B(m,side*.19f,-.18f,-.42f,.075f,.21f,.085f,"#E4D3A6");}if(undead){B(m,-.14f,.17f,-.291f,.25f,.17f,.02f,"#7E9A5B");B(m,-.17f,.04f,-.31f,.16f,.13f,.02f,"#DDD5B4");}});
            for(int i=0;i<2;i++){int side=i==0?-1:1;legs.Add(Part("Piglin leg "+i,new Vector3(side*.15f,.7f,0),m=>{B(m,0,-.28f,0,.24f,.58f,.27f,cloth);B(m,0,-.57f,-.025f,.26f,.14f,.32f,"#4A473B");}));arms.Add(Part("Piglin arm "+i,new Vector3(side*.42f,1.29f,0),m=>{B(m,0,-.29f,0,.22f,.65f,.26f,skin);B(m,0,-.05f,0,.24f,.26f,.29f,cloth);}));}
            Part("Golden weapon",new Vector3(0,-.56f,-.05f),m=>Weapon(m,Kind=="piglin_brute"?"axe":"sword","#EACB68"),arms[1]);
        }

        void BuildCopperGolem()
        {
            Height=1.47f;modelFootprint=.5f;
            body=Part("Copper torso",Vector3.zero,m=>{B(m,0,.68f,0,.57f,.49f,.39f,"#BB7950");B(m,0,.69f,-.21f,.32f,.24f,.04f,"#DF9D66");B(m,.17f,.83f,-.24f,.12f,.12f,.025f,"#6B9A7B");});
            head=Part("Copper head",new Vector3(0,1.17f,-.015f),m=>{B(m,0,0,0,.61f,.49f,.53f,"#C78958");B(m,0,.27f,.015f,.65f,.07f,.58f,"#DBA16A");Eyes(m,.17f,.015f,-.28f,.075f,"#314840");B(m,0,-.12f,-.35f,.15f,.2f,.18f,"#AF734C");B(m,-.17f,.11f,.281f,.21f,.18f,.025f,"#78A586");});
            for(int i=0;i<2;i++){int side=i==0?-1:1;legs.Add(Part("Copper foot "+i,new Vector3(side*.17f,.45f,0),m=>{B(m,0,-.18f,0,.21f,.38f,.26f,"#9F6B4A");B(m,0,-.37f,-.05f,.25f,.1f,.34f,"#79A28A");}));arms.Add(Part("Copper arm "+i,new Vector3(side*.4f,.9f,0),m=>{B(m,0,-.24f,0,.2f,.55f,.23f,"#BC8257");B(m,0,-.45f,-.015f,.23f,.16f,.25f,"#D29662");}));}
        }

        void BuildBird()
        {
            bool parrot=Kind=="parrot";Height=parrot?1.24f:1.03f;modelFootprint=.42f;
            string plumage=parrot?"#D95845":"#E5E2CD";
            body=Part("Feathered body",Vector3.zero,m=>{B(m,0,.52f,.09f,parrot? 0.43f: 0.58f,.55f,.66f,plumage);B(m,0,.4f,-.24f,parrot? 0.36f: 0.49f,.33f,.1f,parrot?"#D09B43":"#F2EDD5");});
            head=Part("Bird head",new Vector3(0,parrot? 0.96f: 0.87f,-.2f),m=>{B(m,0,0,0,.37f,.38f,.39f,plumage);if(parrot){B(m,-.197f,.01f,-.1f,.035f,.24f,.22f,"#E3D8BA");B(m,.197f,.01f,-.1f,.035f,.24f,.22f,"#E3D8BA");B(m,0,.215f,.035f,.18f,.08f,.27f,"#E66A48");}B(m,0,-.06f,-.27f,.21f,.13f,.23f,parrot?"#665449":"#DFAE45");Eyes(m,.115f,.05f,-.208f,.065f);if(!parrot){B(m,0,-.19f,-.23f,.12f,.16f,.1f,"#BC5547");B(m,0,.22f,-.04f,.12f,.11f,.25f,"#CC6250");}});
            for(int i=0;i<2;i++){int side=i==0?-1:1;wings.Add(Part("Bird wing "+i,new Vector3(side*(parrot? 0.23f: 0.32f),.67f,.08f),m=>{B(m,side*.09f,-.13f,.05f,.16f,.43f,.53f,parrot?"#3B8E9F":"#CACFBE");if(parrot){B(m,side*.1f,-.28f,.13f,.17f,.18f,.42f,"#446DB0");B(m,side*.11f,-.07f,-.02f,.18f,.12f,.41f,"#79A947");}}));legs.Add(Part("Bird foot "+i,new Vector3(side*.14f,.27f,-.02f),m=>{B(m,0,-.1f,0,.065f,.24f,.065f,"#B29650");B(m,0,-.21f,-.1f,.19f,.04f,.24f,"#D6B761");}));}
            tails.Add(Part("Bird tail",new Vector3(0,.5f,.42f),m=>{B(m,0,parrot?-.14f: 0.12f,.12f,parrot? 0.18f: 0.34f,parrot? 0.55f: 0.25f,.29f,parrot?"#3969A5":"#D3D7C5");}));
        }
        void BuildFish()
        {
            bool axolotl=Kind=="axolotl",dolphin=Kind=="dolphin",puffer=Kind=="pufferfish",tadpole=Kind=="tadpole",salmon=Kind=="salmon",tropical=Kind=="tropical_fish";
            Height=puffer?1:axolotl? 0.63f:dolphin? 0.9f: 0.7f;modelFootprint=puffer? 0.55f:dolphin? 0.56f: 0.41f;
            string skin=axolotl?"#E9B0B7":dolphin?"#7A9B9D":puffer?"#D3BB60":tadpole?"#695E43":salmon?"#B86055":tropical?"#E29842":"#B6A77F";
            float width=axolotl? 0.48f:dolphin? 0.6f:puffer? 0.77f: 0.38f,depth=axolotl?1.02f:dolphin?1.62f:puffer? 0.74f:tadpole? 0.58f:1.04f;
            body=Part("Aquatic body",Vector3.zero,m=>{B(m,0,.4f,0,width,puffer? 0.7f: 0.44f,depth,skin);B(m,0,.22f,-.02f,width*.85f,.11f,depth*.85f,axolotl?"#F4CFCC":dolphin?"#B7D0C7":"#E0D1A4");if(tropical){B(m,0,.4f,-.1f,width+.025f,.46f,.15f,"#F4E5C1");B(m,0,.4f,.29f,width+.025f,.46f,.1f,"#74513B");}if(puffer){for(int i=0;i<4;i++)for(int j=0;j<3;j++){float x=(i-1.5f)*.2f,z=(j-1)*.23f;B(m,x,.79f,z,.08f,.16f,.08f,"#C8A74B");}for(int side=-1;side<=1;side+=2)for(int j=0;j<3;j++)B(m,side*.43f,.45f,(j-1)*.22f,.17f,.07f,.07f,"#C0A04A");}});
            head=Part("Fish face",new Vector3(0,.43f,-depth*.5f),m=>{B(m,0,0,-.04f,width*.94f,.36f,.25f,skin);Eyes(m,width*.33f,.055f,-.18f,.075f);if(dolphin)B(m,0,-.09f,-.28f,.2f,.14f,.4f,"#95B0AD");if(axolotl){for(int side=-1;side<=1;side+=2){for(int j=0;j<3;j++)B(m,side*(.29f+j*.06f),.13f-j*.1f,.02f,.17f,.055f,.1f,"#CD6F8F");B(m,side*.3f,.03f,.02f,.06f,.3f,.1f,"#DB8DA6");}B(m,0,-.1f,-.18f,.12f,.025f,.025f,"#B16E83");}});
            tails.Add(Part("Swimming tail",new Vector3(0,.4f,depth*.5f),m=>{B(m,0,0,.2f,width*.45f,.26f,.43f,skin);if(dolphin)B(m,0,-.02f,.45f,.83f,.055f,.3f,"#68898E");else if(axolotl||tadpole)B(m,0,.04f,.42f,.045f,.43f,.56f,axolotl?"#DC8EA8":"#827451");else B(m,0,.02f,.42f,.075f,.62f,.29f,salmon?"#925348":"#D2BC7C");}));
            if(axolotl){for(int i=0;i<4;i++)legs.Add(Part("Axolotl foot "+i,new Vector3(i%2==0?-.26f: 0.26f,.28f,i<2?-.28f: 0.32f),m=>{B(m,0,-.09f,0,.13f,.17f,.16f,"#D791A6");B(m,0,-.15f,-.06f,.18f,.045f,.19f,"#E9ACBB");}));}
            else if(!tadpole){for(int i=0;i<2;i++){int side=i==0?-1:1;wings.Add(Part("Fish fin "+i,new Vector3(side*width*.5f,.35f,-.15f),m=>RotatedBox(m,new Vector3(side*.16f,0,.06f),new Vector3(.35f,.045f,.31f),new Vector3(0,side*25,side*15),dolphin?"#66878C":"#D6BF7E")));}Part("Dorsal fin",Vector3.zero,m=>B(m,0,.72f,.07f,.055f,.32f,.45f,dolphin?"#597B83":"#B99755"));}
        }
        void BuildWinged()
        {
            bool phantom=Kind=="phantom";Height=phantom? 0.8f:1.05f;modelFootprint=phantom? 0.75f: 0.42f;modelHover=phantom? 0.92f: 0.75f;
            body=Part("Winged torso",Vector3.zero,m=>{B(m,0,.42f,0,phantom? 0.59f: 0.28f,phantom? 0.26f: 0.55f,phantom?1.05f: 0.3f,phantom?"#506776":"#534839");if(phantom)B(m,0,.3f,-.1f,.45f,.08f,.82f,"#A1B7B6");});
            head=Part("Winged face",new Vector3(0,phantom? 0.44f: 0.79f,phantom?-.64f:-.07f),m=>{B(m,0,0,0,phantom? 0.53f: 0.35f,.31f,.39f,phantom?"#657D87":"#64513B");Eyes(m,phantom? 0.16f: 0.1f,.035f,-.21f,phantom? 0.085f: 0.055f,phantom?"#B9D780":"#302D26");if(!phantom)for(int side=-1;side<=1;side+=2){B(m,side*.13f,.23f,.015f,.13f,.27f,.15f,"#69543E");B(m,side*.13f,.23f,-.068f,.07f,.18f,.018f,"#B39172");}});
            for(int i=0;i<2;i++){int side=i==0?-1:1;wings.Add(Part("Broad wing "+i,new Vector3(side*(phantom? 0.28f: 0.12f),.5f,.03f),m=>{float span=phantom?1.25f: 0.73f;B(m,side*span*.5f,0,.13f,span,.055f,phantom? 0.84f: 0.56f,phantom?"#3D5868":"#6A574C");B(m,side*span*.52f,.02f,-.25f,span,.065f,.075f,phantom?"#73929A":"#342F2A");B(m,side*span*.75f,.015f,.46f,span*.45f,.045f,.17f,phantom?"#284857":"#403C35");for(int j=1;j<4;j++)B(m,side*span*j*.25f,.035f,.08f,.045f,.045f,.58f,phantom?"#738B91":"#3C3630");}));}
            tails.Add(Part("Winged tail",new Vector3(0,.38f,phantom? 0.53f: 0.17f),m=>B(m,0,0,phantom? 0.39f: 0.12f,phantom? 0.17f: 0.11f,.08f,phantom? 0.85f: 0.28f,phantom?"#3F5B67":"#453D31")));
        }
        void BuildBee()
        {
            Height=.88f;modelFootprint=.47f;modelHover=.62f;
            body=Part("Bee stripes",Vector3.zero,m=>{B(m,0,.42f,0,.65f,.53f,.83f,"#DAB34B");B(m,0,.42f,.08f,.67f,.55f,.17f,"#6C5136");B(m,0,.42f,.34f,.67f,.55f,.12f,"#6C5136");B(m,0,.39f,.5f,.08f,.08f,.2f,"#514B39");});
            head=Part("Bee face",new Vector3(0,.44f,-.43f),m=>{B(m,0,0,0,.63f,.5f,.13f,"#E2BD58");Eyes(m,.2f,.025f,-.08f,.17f,"#3E3B31");Eyes(m,.21f,.075f,-.1f,.055f,"#F5EAD0");for(int side=-1;side<=1;side+=2){B(m,side*.17f,.31f,-.035f,.055f,.23f,.055f,"#5F4D32");B(m,side*.21f,.42f,-.04f,.13f,.055f,.07f,"#5F4D32");}});
            for(int i=0;i<2;i++){int side=i==0?-1:1;wings.Add(Part("Bee wing "+i,new Vector3(side*.21f,.69f,0),m=>{B(m,side*.25f,0,.01f,.51f,.025f,.51f,"#C8E2DA");B(m,side*.26f,.015f,.05f,.3f,.012f,.3f,"#E4F3E6");}));}
            for(int i=0;i<6;i++)legs.Add(Part("Bee foot "+i,new Vector3(i%2==0?-.24f: 0.24f,.2f,(i/2-1)*.23f),m=>B(m,0,-.09f,0,.055f,.18f,.055f,"#59472E")));
        }
        void BuildFairy()
        {
            bool vex=Kind=="vex";Height=1.13f;modelFootprint=.39f;modelHover=.69f;string skin=vex?"#A1BBC2":"#6CBFDA";
            body=Part("Fairy body",Vector3.zero,m=>{B(m,0,.48f,0,.31f,.42f,.23f,skin);B(m,0,.3f,0,.21f,.17f,.18f,vex?"#7D9DA9":"#7ACDE0");});
            head=Part("Fairy face",new Vector3(0,.94f,-.02f),m=>{B(m,0,0,0,.44f,.42f,.39f,skin);Eyes(m,.12f,.025f,-.21f,.09f,vex?"#563F49":"#E0F5EB");if(vex){B(m,-.12f,.025f,-.23f,.035f,.045f,.015f,"#CC6B67");B(m,.12f,.025f,-.23f,.035f,.045f,.015f,"#CC6B67");}for(int side=-1;side<=1;side+=2)B(m,side*.25f,.02f,0,.12f,.17f,.16f,skin);});
            for(int i=0;i<2;i++){int side=i==0?-1:1;arms.Add(Part("Fairy arm "+i,new Vector3(side*.24f,.65f,0),m=>B(m,0,-.16f,0,.1f,.37f,.12f,skin)));legs.Add(Part("Fairy leg "+i,new Vector3(side*.09f,.28f,0),m=>B(m,0,-.1f,0,.1f,.21f,.13f,skin)));wings.Add(Part("Fairy wing "+i,new Vector3(side*.12f,.62f,.16f),m=>{RotatedBox(m,new Vector3(side*.29f,.1f,.04f),new Vector3(.57f,.38f,.035f),new Vector3(0,0,side*23),vex?"#6A8495":"#C8EBE4");B(m,side*.28f,-.17f,.055f,.34f,.19f,.03f,vex?"#8AA8B1":"#95D7DF");}));}
            if(vex)Part("Vex blade",new Vector3(0,-.3f,0),m=>Weapon(m,"sword","#C9D8D3"),arms[1]).localScale=Vector3.one*.6f;
        }
        void BuildGhast()
        {
            bool happy=Kind=="happy_ghast";Height=2.5f;modelFootprint=1.12f;
            body=Part("Ghast cube",new Vector3(0,1.4f,0),m=>{B(m,0,0,0,1.72f,1.62f,1.62f,"#DDDCCD");Mottle(m,Vector3.zero,new Vector3(1.72f,1.62f,1.62f),7,7,happy?81:13,"#ECE9D6","#C2C7BD");});
            head=Part("Ghast expression",new Vector3(0,1.4f,-.825f),m=>{Eyes(m,.42f,.16f,-.015f,happy? 0.17f: 0.22f,happy?"#6B8F95":"#6E7371");if(happy){B(m,0,-.29f,-.025f,.45f,.075f,.025f,"#7D8E87");B(m,-.24f,-.23f,-.025f,.075f,.14f,.025f,"#7D8E87");B(m,.24f,-.23f,-.025f,.075f,.14f,.025f,"#7D8E87");}else{B(m,0,-.23f,-.025f,.3f,.25f,.025f,"#7C6260");B(m,-.43f,-.05f,-.025f,.09f,.32f,.025f,"#AFB9B5");B(m,.43f,-.05f,-.025f,.09f,.32f,.025f,"#AFB9B5");}});
            for(int i=0;i<9;i++){float len=.43f+(i%3)*.13f;legs.Add(Part("Ghast tendril "+i,new Vector3((i%3-1)*.52f,.62f,(i/3-1)*.48f),m=>{B(m,0,-len*.5f,0,.19f,len,.2f,"#C8D0C7");B(m,0,-len,.01f,.16f,.11f,.17f,"#E2E2D3");}));}
            if(happy)Part("Friendly harness",Vector3.zero,m=>{for(int side=-1;side<=1;side+=2){B(m,side*.88f,1.3f,0,.05f,.17f,1.66f,"#C38B55");B(m,side*.62f,2.24f,0,.13f,.07f,1.7f,"#BA8753");}B(m,0,1.32f,.84f,1.76f,.17f,.05f,"#C38B55");B(m,0,2.32f,-.15f,.91f,.13f,.47f,"#927148");});
        }
        void BuildElemental()
        {
            bool breeze=Kind=="breeze";Height=1.9f;modelFootprint=.73f;modelHover=.53f;
            head=Part("Elemental head",new Vector3(0,1.4f,0),m=>{B(m,0,0,0,.58f,.56f,.55f,breeze?"#787897":"#D4A443");Eyes(m,.16f,.03f,-.291f,.095f,breeze?"#E8D4A4":"#4B3B2B");B(m,0,-.16f,-.29f,.23f,.065f,.025f,breeze?"#B8B5C7":"#6D4B2A");if(breeze){B(m,-.13f,.33f,0,.56f,.14f,.49f,"#B1ADC0");B(m,.13f,.47f,.04f,.38f,.14f,.36f,"#D0C7D2");}});
            for(int ring=0;ring<3;ring++){int r=ring;orbitParts.Add(Part("Elemental orbit "+ring,new Vector3(0,.35f+ring*.4f,0),m=>{for(int n=0;n<4;n++){float a=n*Mathf.PI*.5f+r*.7f;float radius=breeze? 0.5f+r*.08f: 0.56f;Vector3 at=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);if(breeze)RotatedBox(m,at,new Vector3(.61f,.18f,.32f),new Vector3(0,-a*Mathf.Rad2Deg,0),r==0?"#8F9CA7":r==1?"#BAC4C9":"#6F8493");else{m.Box(at,new Vector3(.13f,.54f,.13f),VoxelAssets.Hex("#D8A742"));m.Box(at+Vector3.up*.28f,new Vector3(.15f,.09f,.15f),VoxelAssets.Hex("#F5CF62"));}}}));}
        }
        void BuildSquid()
        {
            bool glow=Kind=="glow_squid";Height=1.6f;modelFootprint=.65f;
            body=Part("Squid mantle",new Vector3(0,1,0),m=>{B(m,0,0,0,.71f,.93f,.7f,glow?"#427D86":"#596D73");B(m,0,.49f,0,.6f,.1f,.6f,glow?"#69ACA6":"#697E83");Mottle(m,Vector3.zero,new Vector3(.71f,.93f,.7f),5,7,54,glow?"#9BE8CC":"#77939A",glow?"#39707D":"#465F6B");Eyes(m,.23f,-.2f,-.37f,.13f,"#DCE1CA");Eyes(m,.23f,-.2f,-.39f,.065f,"#233F43");});
            for(int i=0;i<8;i++){float a=i*Mathf.PI*.25f;legs.Add(Part("Squid arm "+i,new Vector3(Mathf.Cos(a)*.29f,.55f,Mathf.Sin(a)*.29f),m=>{B(m,0,-.22f,0,.105f,.49f,.12f,glow?"#77C4B6":"#59707A");B(m,0,-.45f,.075f,.08f,.16f,.23f,glow?"#B4E9CC":"#8DA2A6");}));}
        }
        void BuildNautilus()
        {
            bool zombie=Kind=="zombie_nautilus";Height=1.62f;modelFootprint=.75f;
            body=Part("Spiral nautilus shell",new Vector3(0,.93f,.13f),m=>{B(m,0,0,0,1.08f,1.18f,1.12f,zombie?"#9C9A79":"#D6BF8E");B(m,0,.64f,.08f,.9f,.15f,.9f,zombie?"#AFAC86":"#E7D0A1");for(int side=-1;side<=1;side+=2){string line=zombie?"#5A7458":"#9D754D";for(int ring=0;ring<3;ring++){float s=.89f-ring*.26f;B(m,side*.555f,s*.47f,0,.025f,.09f,s,line);B(m,side*.555f,0,s*.47f,.025f,s,.09f,line);B(m,side*.555f,-s*.47f,.1f,.025f,.09f,s-.12f,line);B(m,side*.555f,.03f,-s*.47f,.025f,s-.18f,.09f,line);}}});
            head=Part("Nautilus face",new Vector3(0,.61f,-.64f),m=>{B(m,0,0,0,.7f,.49f,.42f,zombie?"#7C9980":"#CFA18A");Eyes(m,.25f,.1f,-.23f,.1f,zombie?"#D9D995":"#34423A");});
            for(int i=0;i<6;i++){float x=(i%3-1)*.2f;legs.Add(Part("Nautilus tentacle "+i,new Vector3(x,.5f+(i/3)*.17f,-.85f),m=>{B(m,0,-.03f,-.25f,.075f,.08f,.5f,zombie?"#6D8D72":"#D5A48B");B(m,0,.045f,-.49f,.065f,.18f,.07f,zombie?"#93AE85":"#E9C1A0");}));}
        }

        void BuildEnderman()
        {
            Height=3.25f;modelFootprint=.55f;
            body=Part("Lanky torso",Vector3.zero,m=>{B(m,0,2.03f,0,.43f,.92f,.28f,"#253034");B(m,0,2.05f,-.15f,.31f,.65f,.022f,"#30373E");});
            head=Part("Enderman head",new Vector3(0,2.87f,-.025f),m=>{B(m,0,0,0,.61f,.58f,.58f,"#222A30");B(m,-.17f,-.015f,-.306f,.2f,.055f,.025f,"#AD78CE");B(m,.17f,-.015f,-.306f,.2f,.055f,.025f,"#AD78CE");B(m,-.16f,-.015f,-.323f,.09f,.055f,.015f,"#E4B9F1");B(m,.16f,-.015f,-.323f,.09f,.055f,.015f,"#E4B9F1");});
            for(int i=0;i<2;i++){int side=i==0?-1:1;legs.Add(Part("Long leg "+i,new Vector3(side*.14f,1.61f,0),m=>{B(m,0,-.75f,0,.12f,1.53f,.15f,"#242F33");B(m,0,-1.51f,-.025f,.14f,.12f,.23f,"#303A3F");}));arms.Add(Part("Long arm "+i,new Vector3(side*.34f,2.43f,0),m=>B(m,0,-.85f,0,.115f,1.78f,.14f,"#293237")));}
        }
        void BuildShulker()
        {
            Height=1.38f;modelFootprint=.68f;
            body=Part("Shulker lower shell",Vector3.zero,m=>{B(m,0,.32f,0,1.05f,.59f,1.04f,"#805C8C");B(m,0,.065f,0,1.09f,.13f,1.08f,"#6E4F78");Mottle(m,new Vector3(0,.32f,0),new Vector3(1.05f,.59f,1.04f),6,4,57,"#A17AA5","#694B78");});
            head=Part("Shulker inside face",new Vector3(0,.7f,-.12f),m=>{B(m,0,0,0,.53f,.45f,.53f,"#C7C899");Eyes(m,.14f,.035f,-.28f,.08f,"#424C3B");});
            orbitParts.Add(Part("Shulker lid",new Vector3(0,1.05f,0),m=>{B(m,0,0,0,1.09f,.43f,1.08f,"#9870A0");B(m,0,.24f,0,.98f,.09f,.98f,"#AB87B0");B(m,0,-.2f,0,1.12f,.09f,1.11f,"#6A507A");}));
        }
        void BuildGuardian()
        {
            bool elder=Kind=="elder_guardian";Height=1.55f;modelFootprint=.76f;modelHover=.36f;
            string skin=elder?"#B8B99F":"#719A91",spike=elder?"#A58B72":"#D09A60";
            body=Part("Guardian armored cube",Vector3.zero,m=>{B(m,0,.68f,0,1.07f,.95f,1.05f,skin);Mottle(m,new Vector3(0,.68f,0),new Vector3(1.07f,.95f,1.05f),5,5,36,elder?"#D0CDB3":"#99B5A6",elder?"#909E8C":"#557E79");for(int side=-1;side<=1;side+=2){for(int z=-1;z<=1;z++){B(m,side*.63f,.65f,z*.34f,.28f,.11f,.13f,spike);B(m,side*.35f,1.27f,z*.31f,.12f,.29f,.13f,spike);}}B(m,0,.08f,0,.14f,.29f,.14f,spike);});
            head=Part("Single guardian eye",new Vector3(0,.77f,-.55f),m=>{B(m,0,0,0,.55f,.43f,.045f,elder?"#D9D3B9":"#BBC9AB");B(m,0,0,-.03f,.25f,.27f,.025f,"#B66F55");B(m,0,0,-.049f,.11f,.18f,.018f,"#34433D");});
            Transform parent=figure;
            for(int i=0;i<3;i++){int n=i;Transform tail=Part("Guardian tail "+i,i==0?new Vector3(0,.67f,.61f):new Vector3(0,0,.45f),m=>{B(m,0,0,.16f,.24f-n*.055f,.23f-n*.04f,.47f,skin);if(n==2)B(m,0,0,.44f,.07f,.55f,.29f,spike);},parent);tails.Add(tail);parent=tail;}
        }
        void BuildArthropod()
        {
            bool mite=Kind=="endermite";Height=.64f;modelFootprint=.49f;
            body=Part("Segmented insect",Vector3.zero,m=>{for(int i=0;i<5;i++){float w=.38f-Mathf.Abs(i-2)*.055f;B(m,0,.24f,(i-2)*.21f,w,.28f-Mathf.Abs(i-2)*.025f,.19f,mite?(i%2==0?"#78627E":"#5B4D68"):(i%2==0?"#9DABA5":"#788F8A"));B(m,0,.4f,(i-2)*.21f,.06f,.13f,.07f,mite?"#B885B8":"#BBC7B5");}});
            head=Part("Small insect eyes",new Vector3(0,.27f,-.55f),m=>{B(m,0,0,0,.3f,.22f,.2f,mite?"#78647D":"#A6B7AD");Eyes(m,.09f,.035f,-.115f,.05f,mite?"#D29ACB":"#3F5650");});
            for(int i=0;i<8;i++){int side=i%2==0?-1:1;legs.Add(Part("Insect leg "+i,new Vector3(side*.17f,.22f,(i/2-1.5f)*.23f),m=>{B(m,side*.1f,-.04f,0,.23f,.045f,.045f,mite?"#9D79A1":"#849D94");B(m,side*.2f,-.1f,0,.04f,.15f,.04f,mite?"#67536B":"#627B72");}));}
        }
        void BuildTurtle()
        {
            Height=.95f;modelFootprint=.74f;
            body=Part("Turtle shell",Vector3.zero,m=>{B(m,0,.39f,0,1.05f,.5f,1.26f,"#5E8E55");B(m,0,.67f,0,.84f,.12f,1.04f,"#7AA160");B(m,0,.23f,-.02f,1.09f,.14f,1.29f,"#CAB97F");for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++)B(m,i*.26f,.745f,j*.31f,.22f,.025f,.26f,(i+j)%2==0?"#567D49":"#91AD6C");});
            head=Part("Turtle head",new Vector3(0,.4f,-.81f),m=>{B(m,0,0,0,.43f,.3f,.42f,"#7F9F67");Eyes(m,.14f,.035f,-.23f,.055f);});
            for(int i=0;i<4;i++){int side=i%2==0?-1:1;legs.Add(Part("Turtle flipper "+i,new Vector3(side*.48f,.24f,i<2?-.39f: 0.42f),m=>B(m,side*.18f,0,0,.49f,.1f,.28f,"#86A26C")));}
            tails.Add(Part("Turtle tail",new Vector3(0,.28f,.67f),m=>B(m,0,0,.11f,.13f,.12f,.26f,"#7D9760")));
        }
        void BuildFrog()
        {
            Height=.84f;modelFootprint=.62f;
            body=Part("Frog body",Vector3.zero,m=>{B(m,0,.32f,.13f,.73f,.42f,.72f,"#B78E5C");B(m,0,.14f,-.04f,.64f,.13f,.62f,"#D6C18D");});
            head=Part("Wide frog face",new Vector3(0,.47f,-.2f),m=>{B(m,0,0,0,.82f,.32f,.54f,"#C19A69");for(int side=-1;side<=1;side+=2){B(m,side*.27f,.21f,-.03f,.24f,.2f,.23f,"#C49F70");B(m,side*.27f,.21f,-.16f,.13f,.1f,.025f,"#E1D6AA");B(m,side*.27f,.21f,-.178f,.055f,.09f,.018f,"#3C4634");}B(m,0,-.08f,-.285f,.63f,.025f,.018f,"#7D6B43");});
            for(int i=0;i<4;i++){int side=i%2==0?-1:1;bool back=i>=2;legs.Add(Part("Frog foot "+i,new Vector3(side*(back? 0.4f: 0.31f),.28f,back? 0.4f:-.29f),m=>{B(m,side*.09f,-.05f,.02f,back? 0.34f: 0.16f,.2f,back? 0.4f: 0.24f,"#9D8253");B(m,side*.11f,-.16f,-.15f,.28f,.045f,.33f,"#C4A66D");}));}
            tongue=Part("Frog tongue",new Vector3(0,.35f,-.5f),m=>B(m,0,0,-.5f,.085f,.04f,1,"#E59B91"));tongue.gameObject.SetActive(false);
        }
        void BuildRabbit()
        {
            Height=1.14f;modelFootprint=.42f;
            body=Part("Rabbit haunches",Vector3.zero,m=>{B(m,0,.39f,.12f,.47f,.46f,.55f,"#BB9A75");B(m,0,.42f,-.06f,.36f,.32f,.31f,"#DDC4A0");});
            head=Part("Rabbit face",new Vector3(0,.69f,-.27f),m=>{B(m,0,0,0,.39f,.36f,.35f,"#CAA982");Eyes(m,.11f,.035f,-.19f,.055f);B(m,0,-.07f,-.22f,.19f,.13f,.1f,"#EAD9B8");B(m,0,-.03f,-.28f,.07f,.05f,.025f,"#C9918B");});
            for(int i=0;i<2;i++){int side=i==0?-1:1;earParts.Add(Part("Rabbit ear "+i,new Vector3(side*.12f,.18f,0),m=>{B(m,0,.19f,0,.115f,.4f,.13f,"#B99571");B(m,0,.19f,-.073f,.055f,.29f,.02f,"#D9ABA0");},head));}
            for(int i=0;i<4;i++){int side=i%2==0?-1:1;bool back=i>=2;legs.Add(Part("Rabbit foot "+i,new Vector3(side*.2f,.3f,back? 0.3f:-.19f),m=>{B(m,0,-.1f,0,.13f,.23f,.15f,"#B39570");B(m,0,-.21f,-.09f,back? 0.2f: 0.13f,.065f,back? 0.36f: 0.21f,"#DCC5A0");}));}
            tails.Add(Part("Cotton tail",new Vector3(0,.44f,.46f),m=>B(m,0,0,0,.23f,.25f,.24f,"#EDE2C6")));
        }
        void BuildArmadillo()
        {
            Height=.94f;modelFootprint=.58f;
            body=Part("Armadillo armor bands",Vector3.zero,m=>{B(m,0,.46f,.05f,.77f,.58f,.93f,"#A87E68");for(int i=0;i<5;i++)B(m,0,.49f,-.37f+i*.19f,.79f,.6f,.07f,"#C2967A");B(m,0,.79f,.08f,.59f,.08f,.69f,"#BA8C72");});
            head=Part("Armadillo snout",new Vector3(0,.4f,-.56f),m=>{B(m,0,0,0,.42f,.35f,.35f,"#BA9379");B(m,0,-.08f,-.27f,.22f,.17f,.29f,"#CDA58B");Eyes(m,.13f,.05f,-.19f,.055f);for(int side=-1;side<=1;side+=2)B(m,side*.15f,.23f,.01f,.1f,.17f,.12f,"#BA8F77");});
            for(int i=0;i<4;i++)legs.Add(Part("Armadillo leg "+i,new Vector3(i%2==0?-.25f: 0.25f,.26f,i<2?-.27f: 0.35f),m=>{B(m,0,-.09f,0,.15f,.21f,.18f,"#8E6D5C");B(m,0,-.18f,-.08f,.18f,.07f,.25f,"#C9B297");}));
            tails.Add(Part("Banded tail",new Vector3(0,.35f,.54f),m=>{B(m,0,-.05f,.24f,.13f,.13f,.53f,"#B98C71");for(int i=0;i<4;i++)B(m,0,-.05f,.08f+i*.12f,.145f,.145f,.04f,"#886B5B");}));
        }
        void BuildSniffer()
        {
            Height=1.89f;modelFootprint=1.06f;
            body=Part("Sniffer mossy back",Vector3.zero,m=>{B(m,0,.96f,.16f,1.26f,1.03f,1.8f,"#8C5849");B(m,0,1.51f,.21f,1.3f,.2f,1.75f,"#5C854B");for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++){B(m,x*.23f,1.66f+((x+z)%2==0? 0.035f:0),.21f+z*.29f,.2f,.16f,.24f,(x+z)%2==0?"#789C55":"#4C733F");}B(m,0,.64f,-.7f,1.08f,.39f,.14f,"#BB7860");});
            head=Part("Sniffer nose",new Vector3(0,.95f,-1.02f),m=>{B(m,0,0,0,1.04f,.67f,.59f,"#A96551");B(m,0,-.12f,-.39f,.87f,.37f,.38f,"#C6AC66");Eyes(m,.34f,.16f,-.315f,.075f,"#343D2D");Eyes(m,.23f,-.1f,-.6f,.095f,"#806F40");for(int side=-1;side<=1;side+=2)B(m,side*.56f,.04f,.01f,.2f,.32f,.29f,"#794A42");});
            for(int i=0;i<6;i++)legs.Add(Part("Sniffer leg "+i,new Vector3(i%2==0?-.45f: 0.45f,.5f,(i/2-1)*.61f),m=>{B(m,0,-.2f,0,.25f,.44f,.3f,"#775744");B(m,0,-.4f,-.03f,.29f,.13f,.35f,"#BC9A64");}));
        }
        void BuildSnowGolem()
        {
            Height=1.99f;modelFootprint=.59f;
            body=Part("Snowballs",Vector3.zero,m=>{B(m,0,.4f,0,.8f,.76f,.77f,"#E0E7DA");B(m,0,1.02f,0,.59f,.58f,.58f,"#F0F0DF");for(int i=0;i<3;i++)B(m,0,.4f+i*.28f,-(i<2? 0.395f: 0.3f),.07f,.07f,.025f,"#55645B");});
            head=Part("Carved pumpkin",new Vector3(0,1.62f,-.015f),m=>{B(m,0,0,0,.64f,.59f,.61f,"#C28235");for(int i=-2;i<=2;i++)B(m,i*.12f,0,-.315f,.045f,.57f,.025f,"#B16B2D");Eyes(m,.17f,.055f,-.34f,.13f,"#4B482D");B(m,0,-.14f,-.34f,.32f,.08f,.025f,"#4B482D");B(m,-.17f,-.09f,-.34f,.075f,.13f,.025f,"#4B482D");B(m,.17f,-.09f,-.34f,.075f,.13f,.025f,"#4B482D");B(m,0,.34f,.025f,.12f,.13f,.12f,"#6E7845");});
            for(int i=0;i<2;i++){int side=i==0?-1:1;arms.Add(Part("Twig arm "+i,new Vector3(side*.32f,1.18f,0),m=>{B(m,side*.34f,0,0,.73f,.07f,.08f,"#70543B");B(m,side*.63f,.12f,0,.065f,.27f,.065f,"#92704B");B(m,side*.52f,0,-.1f,.065f,.065f,.25f,"#92704B");}));}
        }
        void AnimateBestiary(float time, float idle, float strike, float walk)
        {
            float alive = 1 - deathProgress;
            if (modelHover > 0)
                figure.localPosition += Vector3.up * (modelHover + Mathf.Sin(time * 2.8f + seed) * .055f) * alive;
            for (int i = 0; i < wings.Count; i++) {
                float side = i % 2 == 0 ? -1 : 1;
                float speed = modelFamily == "bee" ? 34 : modelFamily == "fairy" ? 18 : modelFamily == "dragon" ? 4.5f : modelFamily == "fish" ? 9 : 11;
                float amplitude = modelFamily == "dragon" ? 29 : modelFamily == "bat" || modelFamily == "phantom" ? 46 : modelFamily == "bird" ? 13 : 28;
                float flap = Mathf.Sin(time * speed + seed) * amplitude * alive;
                wings[i].localRotation = modelFamily == "fairy" || modelFamily == "fish" ? Quaternion.Euler(0, side * flap, 0) : Quaternion.Euler(0, 0, side * flap);
            }
            for (int i = 0; i < tails.Count; i++) {
                float wave = Mathf.Sin(time * (modelFamily == "fish" ? 9 : 4) + seed - i * .65f);
                tails[i].localRotation = Quaternion.Euler(0, wave * (modelFamily == "dragon" ? 9 : 16) * alive, 0);
            }
            for (int i = 0; i < earParts.Count; i++) {
                float side = i % 2 == 0 ? -1 : 1;
                earParts[i].localRotation = modelFamily == "wither" ? Quaternion.Euler(0, Mathf.Sin(time * 2 + seed + i) * 19, 0)
                    : Quaternion.Euler(5 + Mathf.Sin(time * 3 + seed + i) * 9 * alive, 0, side * 8);
            }
            for (int i = 0; i < orbitParts.Count; i++) {
                if (modelFamily == "shulker") orbitParts[i].localPosition = new Vector3(0, 1.02f + strike * .38f + Mathf.Sin(time * 2 + seed) * idle * .045f, 0);
                else orbitParts[i].localRotation = Quaternion.Euler(0, time * (i % 2 == 0 ? 52 : -61) + seed * Mathf.Rad2Deg, Mathf.Sin(time * 2 + i) * 4);
            }
            bool quadruped = modelFamily == "quadruped" || modelFamily == "camel" || modelFamily == "armadillo" || modelFamily == "sniffer";
            for (int i = 0; i < legs.Count; i++) {
                float side = i % 2 == 0 ? -1 : 1;
                float phase = time * 4 + seed + i * .7f;
                if (modelFamily == "ghast" || modelFamily == "squid") {
                    float radial = modelFamily == "squid" ? i * Mathf.PI * .25f : i * 2.4f;
                    float spread = modelFamily == "squid" ? 21 + Mathf.Sin(phase) * 17 : 7 + Mathf.Sin(phase) * 12;
                    legs[i].localRotation = Quaternion.Euler(Mathf.Sin(radial) * spread * alive, 0, Mathf.Cos(radial) * spread * alive);
                }
                else if (modelFamily == "nautilus") legs[i].localRotation = Quaternion.Euler(Mathf.Sin(phase) * 17 * alive, side * 9, 0);
                else if (modelFamily == "turtle") legs[i].localRotation = Quaternion.Euler(0, walk * side * (movementBlend * 28 + idle * 6), 0);
                else if (quadruped) {
                    float diagonal = (i % 4 == 0 || i % 4 == 3) ? 1 : -1;
                    legs[i].localRotation = Quaternion.Euler(walk * diagonal * (movementBlend * 28 + idle * 5), 0, 0);
                }
                else if (modelFamily == "rabbit" || modelFamily == "frog")
                    legs[i].localRotation = Quaternion.Euler(walk * (i < 2 ? -1 : 1) * (movementBlend * 23 + idle * 4), 0, 0);
            }
            if (modelFamily == "rabbit" || modelFamily == "frog")
                figure.localPosition += Vector3.up * Mathf.Max(0, walk) * (movementBlend * .2f + idle * .025f) * alive;
            if (modelFamily == "warden" || modelFamily == "creaking")
                for (int i = 0; i < arms.Count; i++) arms[i].localRotation = Quaternion.Euler(-walk * (i == 0 ? -1 : 1) * movementBlend * 18 + strike * 75, 0, (i == 0 ? -1 : 1) * 6);
            if (tongue != null) {
                tongue.gameObject.SetActive(!Dead && strike > .08f);
                tongue.localScale = new Vector3(1, 1, .1f + strike * 1.8f);
            }
        }

        void BuildDragon()
        {
            Height = 2.1f; modelFootprint = 1.23f;
            body = Part("Dragon body", new Vector3(0, 1.05f, .1f), m => {
                B(m, 0, 0, 0, .85f, .76f, 1.9f, "#302F3D"); B(m, 0, -.31f, -.1f, .61f, .15f, 1.5f, "#696174");
                for (int i = 0; i < 5; i++) B(m, 0, .45f, -.7f + i * .36f, .16f, .26f, .18f, "#817C8C");
            });
            head = Part("Dragon horned head", new Vector3(0, 1.24f, -1.26f), m => {
                B(m, 0, 0, 0, .69f, .61f, .77f, "#342F40"); B(m, 0, -.17f, -.51f, .56f, .28f, .53f, "#40384D");
                Eyes(m, .24f, .08f, -.4f, .12f, "#C68BEC"); Eyes(m, .16f, -.1f, -.795f, .075f, "#77617F");
                for (int s = -1; s <= 1; s += 2) {
                    RotatedBox(m, new Vector3(s * .28f, .43f, .13f), new Vector3(.12f, .45f, .13f), new Vector3(-25, 0, s * 15), "#A49DAA");
                    B(m, s * .2f, -.34f, -.58f, .065f, .12f, .065f, "#D1C8C8");
                }
            });
            for (int i = 0; i < 2; i++) {
                int s = i == 0 ? -1 : 1;
                wings.Add(Part("Dragon wing " + i, new Vector3(s * .41f, 1.31f, -.12f), m => {
                    RotatedBox(m, new Vector3(s * .86f, 0, -.05f), new Vector3(1.8f, .12f, .13f), new Vector3(0, s * 15, 0), "#77707D");
                    for (int n = 0; n < 4; n++) {
                        float x = s * (.24f + n * .43f), length = 1.45f - n * .22f;
                        B(m, x, -.035f, length * .36f, .45f, .045f, length, n % 2 == 0 ? "#51435E" : "#463B53");
                        RotatedBox(m, new Vector3(x, 0, length * .37f), new Vector3(.065f, .07f, length), new Vector3(0, s * -8, 0), "#91828D");
                    }
                }));
            }
            Transform tailParent = figure;
            for (int i = 0; i < 5; i++) {
                int n = i;
                Transform tail = Part("Dragon tail " + i, i == 0 ? new Vector3(0, 1.05f, 1.08f) : new Vector3(0, 0, .53f), m => {
                    B(m, 0, 0, .2f, .43f - n * .065f, .39f - n * .055f, .6f, "#3B3445");
                    B(m, 0, .25f - n * .035f, .2f, .1f, .2f - n * .025f, .12f, "#8A7C91");
                }, tailParent);
                tails.Add(tail); tailParent = tail;
            }
            for (int i = 0; i < 4; i++) legs.Add(Part("Dragon leg " + i, new Vector3(i % 2 == 0 ? -.42f : .42f, .91f, i < 2 ? -.55f : .69f), m => {
                B(m, 0, -.23f, 0, .23f, .49f, .27f, "#39333F"); B(m, 0, -.47f, -.13f, .27f, .15f, .43f, "#6C6173");
                for (int c = -1; c <= 1; c++) B(m, c * .08f, -.48f, -.36f, .045f, .08f, .13f, "#C7B9BC");
            }));
        }

        void BuildWarden()
        {
            Height = 3.06f; modelFootprint = .92f;
            body = Part("Warden ribbed torso", Vector3.zero, m => {
                B(m, 0, 1.4f, 0, 1.13f, 1.1f, .69f, "#173C41"); B(m, 0, 1.45f, -.37f, .53f, .74f, .07f, "#24646A");
                for (int r = 0; r < 4; r++) for (int s = -1; s <= 1; s += 2)
                    RotatedBox(m, new Vector3(s * .27f, 1.75f - r * .19f, -.42f), new Vector3(.39f, .095f, .09f), new Vector3(0, 0, s * 15), "#A6BAA1");
                B(m, 0, 1.46f, -.45f, .19f, .35f, .04f, "#7CD6C7"); B(m, 0, .91f, 0, .8f, .18f, .56f, "#112B35");
            });
            head = Part("Warden eyeless head", new Vector3(0, 2.33f, -.09f), m => {
                B(m, 0, 0, 0, .82f, .64f, .69f, "#17464B"); B(m, 0, -.18f, -.36f, .6f, .14f, .055f, "#0E2630");
                for (int i = -2; i <= 2; i++) B(m, i * .1f, -.14f, -.4f, .055f, .075f, .035f, "#93AD9D");
                for (int s = -1; s <= 1; s += 2) {
                    B(m, s * .53f, .15f, 0, .29f, .2f, .25f, "#245C5F"); B(m, s * .66f, .38f, .01f, .13f, .46f, .16f, "#4D9A95");
                    B(m, s * .64f, .64f, .01f, .15f, .14f, .15f, "#A7D4BC");
                }
            });
            for (int i = 0; i < 2; i++) {
                int s = i == 0 ? -1 : 1;
                legs.Add(Part("Warden leg " + i, new Vector3(s * .32f, .92f, 0), m => { B(m, 0, -.36f, 0, .4f, .77f, .46f, "#1B3C41"); B(m, 0, -.78f, -.06f, .45f, .18f, .57f, "#123039"); }));
                arms.Add(Part("Warden massive arm " + i, new Vector3(s * .77f, 1.88f, 0), m => {
                    B(m, 0, -.25f, 0, .46f, .66f, .5f, "#285257"); B(m, 0, -.77f, -.02f, .39f, .62f, .43f, "#1B3C45");
                    B(m, 0, -1.12f, -.06f, .47f, .22f, .49f, "#19363D"); B(m, s * .18f, -.29f, -.26f, .1f, .39f, .035f, "#60958B");
                }));
            }
        }

        void BuildWither()
        {
            Height = 2.2f; modelFootprint = 1.0f;
            body = Part("Wither skeletal body", Vector3.zero, m => {
                B(m, 0, .83f, .05f, .22f, 1.17f, .27f, "#33343D"); B(m, 0, 1.33f, 0, 1.58f, .18f, .3f, "#41404A");
                for (int i = 0; i < 3; i++) B(m, 0, 1.12f - i * .26f, -.03f, .85f - i * .16f, .11f, .38f, "#55525D");
            });
            for (int i = 0; i < 3; i++) {
                Transform skull = Part("Wither skull " + i, new Vector3((i - 1) * .78f, i == 1 ? 1.84f : 1.62f, -.05f), m => {
                    B(m, 0, 0, 0, .56f, .55f, .52f, "#36343F"); B(m, 0, -.22f, -.035f, .43f, .16f, .43f, "#48424E");
                    Eyes(m, .14f, .045f, -.28f, .12f, "#CDD0CF"); B(m, 0, -.16f, -.28f, .31f, .07f, .025f, "#ADB2B5");
                    B(m, 0, -.06f, -.28f, .065f, .08f, .03f, "#171C25");
                });
                if (i == 1) head = skull; else earParts.Add(skull);
            }
            tails.Add(Part("Wither spine tip", new Vector3(0, .28f, .04f), m => B(m, 0, -.12f, .15f, .13f, .32f, .37f, "#44404A")));
        }

        void BuildCreaking()
        {
            Height = 2.72f; modelFootprint = .66f;
            body = Part("Creaking twisted trunk", Vector3.zero, m => {
                B(m, 0, 1.4f, 0, .62f, 1.05f, .44f, "#817764"); B(m, -.18f, 1.6f, -.24f, .12f, .73f, .04f, "#4C4A3E");
                B(m, .2f, 1.35f, -.24f, .12f, .6f, .035f, "#B1A389"); B(m, -.12f, 1.95f, .02f, .39f, .32f, .33f, "#6B6655");
            });
            head = Part("Creaking three eyed face", new Vector3(-.09f, 2.22f, -.025f), m => {
                B(m, 0, 0, 0, .55f, .56f, .46f, "#90846D"); B(m, -.14f, .04f, -.245f, .12f, .09f, .03f, "#FFBF45");
                B(m, .13f, .13f, -.245f, .11f, .08f, .03f, "#F5A63D"); B(m, .15f, -.1f, -.245f, .1f, .08f, .03f, "#FFCF68");
                B(m, -.19f, .36f, .06f, .12f, .25f, .14f, "#6E7158"); B(m, .2f, .31f, .08f, .09f, .16f, .13f, "#A4977B");
            });
            for (int i = 0; i < 2; i++) {
                int s = i == 0 ? -1 : 1;
                legs.Add(Part("Creaking root leg " + i, new Vector3(s * .2f, .94f, 0), m => { B(m, 0, -.4f, 0, .21f, .85f, .25f, "#756B58"); B(m, s * .05f, -.85f, -.06f, .32f, .12f, .4f, "#989077"); }));
                arms.Add(Part("Creaking branch arm " + i, new Vector3(s * .46f, 1.8f + s * .12f, 0), m => {
                    RotatedBox(m, new Vector3(s * .05f, -.45f, 0), new Vector3(.2f, 1.01f, .23f), new Vector3(0, 0, s * 9), "#8D826B");
                    B(m, s * .1f, -.99f, -.05f, .26f, .22f, .29f, "#5E5C49"); B(m, s * .2f, -.19f, .03f, .24f, .09f, .11f, "#AAA084");
                }));
            }
        }

        void BuildStrider()
        {
            Height=1.92f;modelFootprint=.65f;
            body=Part("Strider body",Vector3.zero,m=>{B(m,0,1.13f,0,.91f,.86f,.75f,"#A24F50");B(m,0,.76f,0,.78f,.14f,.64f,"#763B42");Mottle(m,new Vector3(0,1.13f,0),new Vector3(.91f,.86f,.75f),7,6,24,"#BD6660","#874749");});
            head=Part("Strider face and whiskers",new Vector3(0,1.31f,-.39f),m=>{Eyes(m,.27f,.09f,-.02f,.12f,"#D8C7A9");Eyes(m,.27f,.09f,-.04f,.055f,"#463A34");B(m,0,-.17f,-.03f,.39f,.055f,.025f,"#673A3D");for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)RotatedBox(m,new Vector3(side*.62f,.19f-i*.17f,.08f),new Vector3(.4f,.025f,.03f),new Vector3(0,0,side*(i-1.5f)*13),"#C7BCAC");});
            for(int i=0;i<2;i++)legs.Add(Part("Strider long leg "+i,new Vector3(i==0?-.27f: 0.27f,.72f,0),m=>{B(m,0,-.32f,0,.1f,.69f,.12f,"#A98E78");B(m,0,-.66f,-.06f,.17f,.08f,.22f,"#544F43");}));
        }

    }
}
