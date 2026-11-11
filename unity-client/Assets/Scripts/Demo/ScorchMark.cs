using System.Collections.Generic;
using UnityEngine;

/// <summary>A dark blast mark left on the ground where a unit died; it fades out slowly. Purely visual.</summary>
public sealed class ScorchMark : MonoBehaviour
{
    private const float Life = 24f, FadeTime = 8f, Alpha = 0.42f;
    private const int MaxMarks = 24;
    private static readonly List<ScorchMark> All = new List<ScorchMark>();
    private static Mesh _disc;

    private Material _material;
    private float _age;

    public static void Spawn(Vector3 at, float radius, Material overlayTemplate)
    {
        if (_disc == null) _disc = MeshFactory.Ring(0f, 1f, 16);

        var go = MeshFactory.Spawn("Scorch", _disc, new Material(overlayTemplate) { color = new Color(0.05f, 0.04f, 0.03f, Alpha) });
        go.transform.position = new Vector3(at.x, 0.03f, at.z);
        go.transform.localScale = new Vector3(radius, 1f, radius * Random.Range(0.8f, 1f));
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.GetComponent<MeshRenderer>().receiveShadows = false;

        var mark = go.AddComponent<ScorchMark>();
        mark._material = go.GetComponent<MeshRenderer>().sharedMaterial;
        All.Add(mark);
        if (All.Count > MaxMarks) Destroy(All[0].gameObject);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= Life) { Destroy(gameObject); return; }
        float fade = Mathf.Clamp01((Life - _age) / FadeTime);
        _material.color = new Color(0.05f, 0.04f, 0.03f, Alpha * fade);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        if (_material != null) Destroy(_material);
    }
}
