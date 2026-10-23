using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        GameState expansionState;
        float expansionSnapshotAt;
        Transform ownedTerritory,ownedTerritoryBeacon;
        int ownedTerritorySlot=-1;

        internal float ExpansionScale
        {
            get {return Mathf.Lerp(1,BattlefieldCatalog.Scale(expansionState==null?null:expansionState.battlefield),ExpansionProgress());}
        }
        internal float ExpansionCameraScale
        {
            get {
                float target=BattlefieldCatalog.Scale(expansionState==null?null:expansionState.battlefield);
                if(expansionState==null||expansionState.phase!="reveal")return target;
                if(expansionState.revealStage=="arena"||expansionState.revealStage=="map")return 1;
                if(expansionState.revealStage!="terrain")return target;
                float age=expansionState.revealStageAge+Mathf.Max(0,Time.unscaledTime-expansionSnapshotAt);
                // Let the perimeter visibly advance first. Pulling the orthographic
                // camera out at the same ratio had kept the board's screen size fixed
                // and only made armies look smaller.
                return Mathf.Lerp(1,target,FieldLayerProgress(age,.45f,2.25f));
            }
        }
        float ExpansionProgress()
        {
            if(expansionState==null||expansionState.battlefield==null)return 0;
            if(expansionState.phase!="reveal")return 1;
            if(expansionState.revealStage=="arena"||expansionState.revealStage=="map")return 0;
            if(expansionState.revealStage!="terrain")return 1;
            float age=expansionState.battlefield.revealAge+Mathf.Max(0,Time.unscaledTime-expansionSnapshotAt);
            return Mathf.SmoothStep(0,1,Mathf.Clamp01(age/FieldFoundationSeconds));
        }
        void SyncArenaExpansion(GameState next)
        {
            expansionState=next;expansionSnapshotAt=Time.unscaledTime;
            UpdateDeploymentTerraces();
        }
        void TickArenaExpansion()
        {
            UpdateDeploymentTerraces();
            if(expansionState==null||expansionState.phase!="reveal")return;
            // The board and every model share this transform each rendered frame;
            // network snapshots cannot make a creature skate ahead of its moving land.
            foreach(var player in expansionState.players){
                if(player.eliminated||player.hp<=0)continue;
                VoxelCreature view;
                if(creatures.TryGetValue("steve:"+player.id,out view)){
                    Point2 at=DisplayDeploymentPoint(player,player.steve);view.Target=WorldSurfacePoint(at.x,at.y);view.transform.position=view.Target;
                }
                foreach(var unit in player.units)if(creatures.TryGetValue("unit:"+unit.id,out view)){
                    Point2 at=DisplayDeploymentPoint(player,new Point2(unit.x,unit.y));view.Target=WorldSurfacePoint(at.x,at.y);view.transform.position=view.Target;
                }
            }
        }
        void UpdateDeploymentTerraces()
        {
            if(expansionState==null)return;
            float progress=ExpansionProgress();
            for(int slot=0;slot<4;slot++){
                Transform terrace=preparations[slot];if(terrace==null)continue;
                PlayerState player=null;for(int i=0;i<expansionState.players.Count;i++)if(expansionState.players[i].slot==slot){player=expansionState.players[i];break;}
                bool settlement=expansionState.phase=="results"||expansionState.phase=="finished";
                bool present=player!=null&&(!player.eliminated&&player.hp>0||settlement&&player.deployed);
                bool preparing=expansionState.phase=="prep"||expansionState.phase=="recruit";
                terrace.gameObject.SetActive(present&&(!preparing||player.id==playerId));
                bool deployed=player!=null&&player.deployed;
                terrace.localPosition=deployed?new Vector3(player.deploymentOffset.x*WorldScale,0,player.deploymentOffset.y*WorldScale)*progress:Vector3.zero;
                terrace.localRotation=Quaternion.Slerp(Quaternion.Euler(0,-slot*90,0),Quaternion.Euler(0,-(deployed?player.deploymentSide:slot)*90,0),progress);
                terrace.localScale=Vector3.one;
            }
            UpdateOwnedTerritory();
        }
        void UpdateOwnedTerritory()
        {
            bool locating=expansionState!=null&&(expansionState.phase=="prep"||expansionState.phase=="recruit"||expansionState.phase=="reveal"||expansionState.phase=="tactics");
            if(!locating){if(ownedTerritory!=null)ownedTerritory.gameObject.SetActive(false);return;}
            PlayerState own=null;for(int i=0;i<expansionState.players.Count;i++)if(expansionState.players[i].id==playerId){own=expansionState.players[i];break;}
            locating=locating&&own!=null&&!own.eliminated&&own.hp>0;
            if(!locating){if(ownedTerritory!=null)ownedTerritory.gameObject.SetActive(false);return;}
            if(ownedTerritory==null){
                ownedTerritory=NewRoot("Your army territory outline",preparations[localSlot]);
                VoxelAssets.Part(ownedTerritory,"Your territory illuminated perimeter",VoxelAssets.Cached("owned-territory-border",b=>{
                    Color team=FieldColor("#B2F3DA"),tip=FieldColor("#FFF4BB");
                    // The frame follows the original full-size deployment terrace.
                    // It never scales with the larger arena or its camera.
                    b.Box(new Vector3(0,.105f,6.31f),new Vector3(16.65f,.045f,.105f),team);
                    b.Box(new Vector3(0,.105f,16.43f),new Vector3(16.65f,.045f,.105f),team);
                    foreach(int side in new[]{-1,1}){
                        b.Box(new Vector3(side*8.28f,.105f,11.37f),new Vector3(.105f,.045f,10.2f),team);
                        foreach(float z in new[]{6.31f,16.43f}){
                            b.Box(new Vector3(side*7.83f,.13f,z),new Vector3(.95f,.07f,.19f),tip);
                            b.Box(new Vector3(side*8.28f,.13f,z+(z<10?.43f:-.43f)),new Vector3(.19f,.07f,.95f),tip);
                        }
                    }
                }),Vector3.zero,VoxelAssets.Glow,false);
                ownedTerritoryBeacon=NewRoot("Your army locating pennant",ownedTerritory);
                VoxelAssets.Part(ownedTerritoryBeacon,"Floating commander chevrons",VoxelAssets.Cached("owned-territory-beacon",b=>{
                    Color gold=FieldColor("#FFF0A6"),mint=FieldColor("#B5F3D8");
                    for(int n=0;n<2;n++){
                        float y=n*.43f;
                        b.Box(new Vector3(-.31f,y+.24f,0),new Vector3(.89f,.16f,.12f),n==0?gold:mint,Quaternion.Euler(0,0,-32));
                        b.Box(new Vector3(.31f,y+.24f,0),new Vector3(.89f,.16f,.12f),n==0?gold:mint,Quaternion.Euler(0,0,32));
                    }
                }),Vector3.zero,VoxelAssets.Glow,false);
            }
            if(ownedTerritorySlot!=localSlot){ownedTerritory.SetParent(preparations[localSlot],false);ownedTerritorySlot=localSlot;}
            ownedTerritory.gameObject.SetActive(true);ownedTerritory.localPosition=Vector3.zero;ownedTerritory.localScale=Vector3.one;
            ownedTerritoryBeacon.localPosition=new Vector3(0,2.5f+Mathf.Sin(Time.unscaledTime*2.6f)*.12f,16.1f);
            // Stay readable when Q/E changes the camera without rotating the land.
            ownedTerritoryBeacon.rotation=Quaternion.Euler(0,-90*localViewSide+90*viewQuarterTurns,0);
        }
        Point2 DisplayDeploymentPoint(PlayerState player,Point2 actual)
        {
            if(player==null||!player.deployed)return actual;
            float progress=ExpansionProgress();if(progress>=.99999f)return actual;
            Point2 local=ArenaGame.WorldToFormation(player,actual.x,actual.y);
            Quaternion rotation=Quaternion.Slerp(Quaternion.Euler(0,-player.slot*90,0),Quaternion.Euler(0,-player.deploymentSide*90,0),progress);
            Vector3 point=rotation*new Vector3(local.x,0,local.y)+new Vector3(player.deploymentOffset.x,0,player.deploymentOffset.y)*progress;
            return new Point2(point.x,point.z);
        }
        void ResetArenaExpansion()
        {
            expansionState=null;expansionSnapshotAt=0;
            if(ownedTerritory!=null)ownedTerritory.gameObject.SetActive(false);
            for(int slot=0;slot<4;slot++)if(preparations[slot]!=null){
                preparations[slot].localPosition=Vector3.zero;preparations[slot].localRotation=Quaternion.Euler(0,-slot*90,0);preparations[slot].localScale=Vector3.one;
            }
        }
    }
}
