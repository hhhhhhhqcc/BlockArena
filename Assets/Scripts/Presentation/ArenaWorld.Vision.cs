using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        internal bool VisionSuppressedForVerification;
        // Ordinary terrain and armies remain visible. Concealment is local to a body
        // or an actual smoke/forest region; there is no screen-wide sight shading.
        void InitializeVisionPresentation() { }
        void RefreshVisionPresentation() { TickConcealmentPresentation(Time.unscaledTime); }
        void ClearVisionPresentation() { ResetConcealmentPresentation(); }
        internal bool VisionFogActive { get { return false; } }
        internal int VisionSourceCount { get { return 0; } }
        internal RenderTexture VisionMask { get { return null; } }
        internal float VisionMaskExtent { get { return 27*ExpansionScale; } }
        public bool IsInArmySight(float x,float y)
        {
            if(state==null||state.phase!="battle"||state.battlefield==null)return true;
            var viewer=state.players.Find(p=>p.id==playerId);
            if(viewer==null||viewer.eliminated)return true;
            bool any=false;
            foreach(var fighter in state.fighters){
                if(fighter.dead||fighter.neutral||fighter.ownerId!=playerId)continue;
                any=true;float dx=x-fighter.x,dy=y-fighter.y,radius=BlockArena.Core.BattlefieldVision.Range(state.battlefield,fighter);
                if(dx*dx+dy*dy<=radius*radius)return true;
            }
            return !any;
        }
    }
}
