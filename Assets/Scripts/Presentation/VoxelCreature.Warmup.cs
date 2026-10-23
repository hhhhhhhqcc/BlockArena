namespace BlockArena.Presentation
{
    public sealed partial class VoxelCreature
    {
        // SetCombatFlash normally discovers these meshes on the first hit/death.
        // Fill the same per-instance references while the model is still inactive;
        // ImpactTint shares each species/part clone through VoxelAssets' cache.
        internal void WarmupCombatPresentation()
        {
            if(combatNormalMeshes==null||combatImpactMeshes==null)return;
            for(int i=0;i<combatNormalMeshes.Length;i++)
                if(combatImpactMeshes[i]==null&&combatNormalMeshes[i]!=null)
                    combatImpactMeshes[i]=VoxelAssets.ImpactTint(combatNormalMeshes[i]);
        }
    }
}
