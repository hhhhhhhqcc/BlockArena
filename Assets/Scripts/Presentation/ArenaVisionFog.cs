using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    /// <summary>GPU union of every living ally's sight; no per-frame screen readback or source cap.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ArenaVisionFog : MonoBehaviour
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> triangles = new List<int>();
        readonly List<Color> colors = new List<Color>();
        Material maskMaterial, fogMaterial;
        RenderTexture mask;
        Mesh sightMesh;
        Camera cameraView;
        GameState renderedState;
        string renderedViewer;
        float maskExtent, arenaRadius, opacity;
        bool active;
        float nextMaskAt;
        public System.Func<bool> CanRender;
        public bool Active { get { return active && (CanRender == null || CanRender()); } }
        public int SourceCount { get; private set; }
        public RenderTexture SightMask { get { return mask; } }

        void Awake()
        {
            cameraView = GetComponent<Camera>();
            cameraView.depthTextureMode |= DepthTextureMode.Depth;
            Shader fogShader = Resources.Load<Shader>("ArenaVisionFog"),
                maskShader = Resources.Load<Shader>("ArenaVisionMask");
            if (fogShader == null || maskShader == null || !fogShader.isSupported || !maskShader.isSupported) return;
            fogMaterial = new Material(fogShader) { name = "Shared army sight shading" };
            maskMaterial = new Material(maskShader) { name = "Unlimited allied sight union" };
            mask = new RenderTexture(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) {
                name = "Army sight field 256x256", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            mask.Create();
            sightMesh = new Mesh { name = "Living allied sight discs", indexFormat = IndexFormat.UInt32 };
            sightMesh.MarkDynamic();
        }

        public void Configure(GameState state, string viewer, bool gameplayVisible, float expansion)
        {
            bool next = gameplayVisible && state != null && state.phase == "battle" && state.battlefield != null && fogMaterial != null;
            PlayerState player = next ? state.players.Find(p => p.id == viewer) : null;
            next = next && player != null && !player.eliminated;
            int count = 0;
            if (next) foreach (var fighter in state.fighters)
                if (!fighter.dead && !fighter.neutral && fighter.ownerId == viewer) count++;
            // Defeated armies spectate; never leave a player watching an entirely dark arena.
            active = next && count > 0;
            if (!active) { SourceCount = 0; renderedState = null; opacity = 0; return; }
            arenaRadius = 24.5f * expansion;
            float extent = 27 * expansion;
            if (renderedState == state && renderedViewer == viewer && Mathf.Approximately(maskExtent,extent) && Time.unscaledTime < nextMaskAt) return;
            nextMaskAt = Time.unscaledTime + .12f;
            maskExtent = extent;
            vertices.Clear(); uv.Clear(); triangles.Clear(); colors.Clear();
            foreach (var fighter in state.fighters) {
                if (fighter.dead || fighter.neutral || fighter.ownerId != viewer) continue;
                float radius = BattlefieldVision.Range(state.battlefield,fighter) / 25;
                float x = fighter.x/25, y = fighter.y/25;
                int index = vertices.Count;
                vertices.Add(MaskPoint(x-radius,y-radius)); vertices.Add(MaskPoint(x+radius,y-radius));
                vertices.Add(MaskPoint(x+radius,y+radius)); vertices.Add(MaskPoint(x-radius,y+radius));
                uv.Add(new Vector2(-1,-1)); uv.Add(new Vector2(1,-1)); uv.Add(new Vector2(1,1)); uv.Add(new Vector2(-1,1));
                Color feather = new Color(Mathf.Min(.18f,45/(radius*25)),0,0,1);
                for (int i=0;i<4;i++) colors.Add(feather);
                triangles.Add(index); triangles.Add(index+1); triangles.Add(index+2);
                triangles.Add(index); triangles.Add(index+2); triangles.Add(index+3);
            }
            sightMesh.Clear(); sightMesh.SetVertices(vertices); sightMesh.SetUVs(0,uv);
            sightMesh.SetColors(colors); sightMesh.SetTriangles(triangles,0); sightMesh.RecalculateBounds();
            RenderTexture previous = RenderTexture.active;
            try {
                Graphics.SetRenderTarget(mask); GL.Clear(false,true,Color.black);
                maskMaterial.SetPass(0); Graphics.DrawMeshNow(sightMesh,Matrix4x4.identity);
            } finally { RenderTexture.active = previous; }
            SourceCount = count; renderedState = state; renderedViewer = viewer;
        }
        Vector3 MaskPoint(float x,float y) { return new Vector3((x+maskExtent)/(2*maskExtent),(y+maskExtent)/(2*maskExtent),0); }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!Active || fogMaterial == null || mask == null) { Graphics.Blit(source,destination); return; }
            opacity = Mathf.MoveTowards(opacity,1,Time.unscaledDeltaTime*4);
            fogMaterial.SetTexture("_SightMask",mask);
            fogMaterial.SetFloat("_MaskExtent",maskExtent);
            fogMaterial.SetFloat("_ArenaRadius",arenaRadius);
            fogMaterial.SetFloat("_Opacity",opacity);
            fogMaterial.SetMatrix("_InverseViewProjection",(GL.GetGPUProjectionMatrix(cameraView.projectionMatrix,true)*cameraView.worldToCameraMatrix).inverse);
            Graphics.Blit(source,destination,fogMaterial);
        }
        void OnDestroy()
        {
            if (mask != null) { mask.Release(); Destroy(mask); }
            if (sightMesh != null) Destroy(sightMesh);
            if (maskMaterial != null) Destroy(maskMaterial);
            if (fogMaterial != null) Destroy(fogMaterial);
        }
    }
}
