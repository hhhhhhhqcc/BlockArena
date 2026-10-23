using System;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        public static Point2 FormationToWorld(PlayerState player,float x,float y)
        {
            if(player==null)return new Point2(x,y);
            var p=LocalToWorld(player.deployed?player.deploymentSide:player.slot,x,y);
            if(player.deployed){p.x+=player.deploymentOffset.x;p.y+=player.deploymentOffset.y;}
            return p;
        }
        public static Point2 WorldToFormation(PlayerState player,float x,float y)
        {
            if(player==null)return new Point2(x,y);
            if(player.deployed){x-=player.deploymentOffset.x;y-=player.deploymentOffset.y;}
            return WorldToLocal(player.deployed?player.deploymentSide:player.slot,x,y);
        }
        float BattlefieldEdge {get{return BattlefieldCatalog.Extent(State.battlefield);}}

        void RestorePreparation(PlayerState player)
        {
            // Unit positions are saved formation positions, not last combat positions.
            // Inverting the old deployment avoids drift over repeated rounds.
            if(player.deployed)foreach(var unit in player.units){
                var local=WorldToFormation(player,unit.x,unit.y);
                var flat=LocalToWorld(player.slot,local.x,local.y);unit.x=flat.x;unit.y=flat.y;
            }
            player.deployed=false;player.deploymentSide=player.slot;player.deploymentOffset=new Point2();
        }
        void DeployExpandedFormations()
        {
            var players=Living().OrderBy(p=>p.slot).ToArray();
            var sides=new[]{0,1,2,3};
            // Draw once on the authoritative host. Clients receive the actual transforms.
            for(int i=sides.Length-1;i>0;i--){int j=random.Next(i+1);int swap=sides[i];sides[i]=sides[j];sides[j]=swap;}
            for(int i=0;i<players.Length;i++){
                var player=players[i];
                RestorePreparation(player);
                var steveLocal=WorldToLocal(player.slot,player.steve.x,player.steve.y);
                int side=sides[i];
                // Stagger each territory toward the next corner so the complete
                // rectangle clears both the frost river and the courtyard ruins.
                // Steve's rear position still stays inside the expanded boundary.
                float lateral=530+(Rng()-.5f)*16, depth=660+(Rng()-.5f)*16;
                var centre=LocalToWorld(side,lateral,depth);
                var reference=LocalToWorld(side,0,265);
                player.deploymentSide=side;player.deploymentOffset=new Point2(centre.x-reference.x,centre.y-reference.y);player.deployed=true;
                foreach(var unit in player.units){
                    var local=WorldToLocal(player.slot,unit.x,unit.y);
                    var at=FormationToWorld(player,local.x,local.y);unit.x=at.x;unit.y=at.y;
                }
                player.steve=FormationToWorld(player,steveLocal.x,steveLocal.y);
                player.steveDestination=player.steve;player.steveNavigating=false;player.moveUntil=0;
            }
        }
    }
}
