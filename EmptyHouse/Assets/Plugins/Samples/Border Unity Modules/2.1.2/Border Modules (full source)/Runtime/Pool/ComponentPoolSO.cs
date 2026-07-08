using UnityEngine;

namespace Border.Pool
{
    public abstract class ComponentPoolSO<T> : PoolSO<T> where T : Component
    {
        private Transform parent;
        private Transform poolRoot;
        private Transform PoolRoot
        {
            get
            {
                if (poolRoot == null)
                {
                    poolRoot = new GameObject(name).transform;
                    poolRoot.SetParent(parent);
                }
                return poolRoot;
            }
        }

        /// <summary>
        /// 풀의 부모 트랜스폼을 지정한다.
        /// 생성/반환된 멤버들의 부모로 설정될 트랜스폼이다.
        /// </summary>
        public void SetParent(Transform p)
        {
            parent = p;
            PoolRoot.SetParent(parent);
        }

        public override T Request()
        {
            T member = base.Request();
            member.gameObject.SetActive(true);
            return member;
        }

        public override void Return(T member)
        {
            member.transform.SetParent(PoolRoot);
            member.gameObject.SetActive(false);
            base.Return(member);
        }

        protected override T Create()
        {
            T newMember = base.Create();
            newMember.transform.SetParent(PoolRoot);
            newMember.gameObject.SetActive(false);
            return newMember;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (poolRoot != null)
            {
                Destroy(poolRoot.gameObject);
            }
        }

        /// <summary>
        /// 베이스의 스택 초기화에 더해, 씬과 함께 파괴된 부모/풀 루트 트랜스폼 참조도 함께 비운다.
        /// 다음 씬에서 SetParent 호출 시 PoolRoot 게터가 새 루트를 다시 만들 수 있도록 한다.
        /// </summary>
        public override void ClearPool()
        {
            base.ClearPool();
            poolRoot = null;
            parent = null;
        }
    }
}
