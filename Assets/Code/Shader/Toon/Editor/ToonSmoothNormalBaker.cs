using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sandbox.Toon
{
    /// <summary>
    /// モデルのインポート時に、同一座標の頂点の法線を平均化した「スムース法線」を計算し、
    /// 接空間に変換して UV8（TEXCOORD7）へ焼き込む。
    /// Sandbox/Toon のアウトラインパスがこれを押し出し方向に使い、ハードエッジでアウトラインが割れるのを防ぐ。
    /// 対象は <see cref="TargetFolder"/> 配下のモデルのみ。
    /// </summary>
    public sealed class ToonSmoothNormalBaker : AssetPostprocessor
    {
        /// <summary> 焼き込み対象とするモデルのフォルダ </summary>
        private const string TargetFolder = "Assets/Art/Model/";

        /// <summary> 焼き込み先の UV チャンネル（シェーダー側の TEXCOORD7 と一致させること） </summary>
        private const int SmoothNormalUVChannel = 7;

        // 焼き込み処理を変更したら値を上げる（対象モデルが再インポートされる）
        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(TargetFolder))
            {
                return;
            }

            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                BakeSmoothNormals(meshFilter.sharedMesh);
            }

            foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                BakeSmoothNormals(skinnedMeshRenderer.sharedMesh);
            }
        }

        /// <summary> メッシュのスムース法線を接空間で UV8 に書き込む </summary>
        private void BakeSmoothNormals(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (normals.Length != vertices.Length)
            {
                Debug.LogWarning($"[ToonSmoothNormalBaker] 法線が無いためスキップしました: {assetPath} / {mesh.name}");
                return;
            }

            if (mesh.tangents.Length != vertices.Length)
            {
                mesh.RecalculateTangents();
            }

            var tangents = mesh.tangents;
            var smoothNormals = AccumulateAngleWeightedNormals(mesh, vertices);
            var bakedNormals = new List<Vector3>(vertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals[i].normalized;
                Vector3 smooth = smoothNormals.TryGetValue(vertices[i], out var accumulated) && accumulated.sqrMagnitude > 0f
                    ? accumulated.normalized
                    : normal;

                // シェーダー側と同じく、接線を法線に直交化してから接空間基底を作る
                Vector4 tangent = tangents[i];
                Vector3 tangentAxis = Vector3.Normalize((Vector3)tangent - normal * Vector3.Dot(normal, tangent));
                Vector3 bitangentAxis = Vector3.Cross(normal, tangentAxis) * (tangent.w < 0f ? -1f : 1f);

                bakedNormals.Add(new Vector3(
                    Vector3.Dot(smooth, tangentAxis),
                    Vector3.Dot(smooth, bitangentAxis),
                    Vector3.Dot(smooth, normal)));
            }

            mesh.SetUVs(SmoothNormalUVChannel, bakedNormals);
        }

        /// <summary>
        /// 全三角形の面法線を頂点の角度で重み付けし、頂点座標ごとに合算する。
        /// 座標が完全一致する頂点（ハードエッジや UV シームで分割された頂点）は同じキーに集約される。
        /// </summary>
        private static Dictionary<Vector3, Vector3> AccumulateAngleWeightedNormals(Mesh mesh, Vector3[] vertices)
        {
            var result = new Dictionary<Vector3, Vector3>(vertices.Length);

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    continue;
                }

                var triangles = mesh.GetTriangles(subMesh);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector3 p0 = vertices[triangles[t]];
                    Vector3 p1 = vertices[triangles[t + 1]];
                    Vector3 p2 = vertices[triangles[t + 2]];

                    Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
                    if (faceNormal.sqrMagnitude <= 0f)
                    {
                        continue;
                    }

                    faceNormal.Normalize();
                    Accumulate(result, p0, faceNormal * Vector3.Angle(p1 - p0, p2 - p0));
                    Accumulate(result, p1, faceNormal * Vector3.Angle(p2 - p1, p0 - p1));
                    Accumulate(result, p2, faceNormal * Vector3.Angle(p0 - p2, p1 - p2));
                }
            }

            return result;
        }

        private static void Accumulate(Dictionary<Vector3, Vector3> map, Vector3 key, Vector3 value)
        {
            map[key] = map.TryGetValue(key, out var current) ? current + value : value;
        }
    }
}
