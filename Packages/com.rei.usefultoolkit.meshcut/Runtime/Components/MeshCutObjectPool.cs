using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UsefulToolkit.Utility;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 破片オブジェクトを事前生成して使い回すプール。
    /// 生成はStartでの非同期処理のため、利用側は WaitForGeneration() で完了を待つこと。
    /// </summary>
    public class MeshCutObjectPool : MonoBehaviour
    {
        public bool IsGenerated { get; private set; }

        [SerializeField] private int _generateCapacity;
        [SerializeField] private GameObject _prefab;

        private RecycleBuffer<CuttableObject> _recycleBuffer;

        private async void Start()
        {
            IsGenerated = false;
            var objects = await InstantiateAsync(_prefab, _generateCapacity, transform);

            CuttableObject[] buffer = new CuttableObject[_generateCapacity];
            for (int i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                obj.SetActive(false);
                buffer[i] = obj.GetComponent<CuttableObject>();
            }

            _recycleBuffer = new RecycleBuffer<CuttableObject>(buffer);
            IsGenerated = true;
        }

        /// <summary>
        /// プールの事前生成が完了するまで待機します。
        /// Start内の InstantiateAsync が完了する前に GetObjects を呼ぶと落ちるため、切断前に必ず待つこと。
        /// </summary>
        public UniTask WaitForGeneration()
        {
            return UniTask.WaitUntil(() => IsGenerated, cancellationToken: destroyCancellationToken);
        }

        public List<CuttableObject> GetObjects(int objectCount)
        {
            if (!IsGenerated)
            {
                Debug.LogError("プールの生成が完了していません。WaitForGeneration()で完了を待ってから呼び出してください。");
                return new List<CuttableObject>();
            }

            if (objectCount > _generateCapacity)
            {
                Debug.LogWarning("オブジェクトの要求量が生成数を超えています");
                objectCount = _generateCapacity;
            }

            List<CuttableObject> results = new(objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                var item = _recycleBuffer.Get();
                // Get内で既にOnRecycleが呼ばれる(使用中の場合)
                results.Add(item);
            }

            return results;
        }

        /// <summary>
        /// このプールが管理しているオブジェクトであればスロットを解放して true を返します。
        /// シーンに直接置かれた切断対象など、プール外のオブジェクトを渡しても安全です。
        /// </summary>
        public bool TryReleaseObject(CuttableObject releaseObject)
        {
            if (releaseObject == null || _recycleBuffer == null) return false;

            return _recycleBuffer.TryRelease(releaseObject);
        }

        public void ReleaseObject(CuttableObject releaseObject)
        {
            if (releaseObject == null) return;
            releaseObject.OnRecycle();
            _recycleBuffer.Release(releaseObject);
        }
    }
}
