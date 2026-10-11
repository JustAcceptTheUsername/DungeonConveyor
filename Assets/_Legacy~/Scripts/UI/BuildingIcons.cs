using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DungeonConveyor
{
    /// <summary>Renders each building model to a small texture for the build bar.</summary>
    public static class BuildingIcons
    {
        const int IconLayer = 31;
        const int Resolution = 160;

        public static void RenderAll(Transform parent)
        {
            var stage = new GameObject("IconStage");
            stage.transform.SetParent(parent, false);
            stage.transform.position = new Vector3(-500, 0, -500);

            var cam = new GameObject("IconCamera").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform, false);
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.075f, 0.068f, 0.078f, 1f);
            cam.cullingMask = 1 << IconLayer;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;

            foreach (var def in Defs.Buildable)
            {
                var model = BuildingVisuals.Create(def, stage.transform);
                if (def == Defs.Conveyor)
                {
                    var (mesh, mats) = BuildingVisuals.BeltMesh(BuildingVisuals.BeltShape.Straight, 1);
                    model.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    model.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
                }
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IconLayer;
                model.localRotation = Quaternion.Euler(0, def.Rotatable ? 90 : 0, 0);

                var bounds = new Bounds(model.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

                var rot = Quaternion.Euler(30, 45, 0);
                cam.transform.rotation = rot;
                cam.transform.position = bounds.center - rot * Vector3.forward * 30f;
                cam.orthographicSize = Mathf.Max(bounds.extents.magnitude, 0.5f) * 0.95f;

                try
                {
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { name = def.Id + "_Icon" };
                    tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    def.Icon = tex;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Icon render failed for {def.Id}: {e.Message}");
                }
                Object.DestroyImmediate(model.gameObject);
            }

            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(stage);
        }
    }
}
