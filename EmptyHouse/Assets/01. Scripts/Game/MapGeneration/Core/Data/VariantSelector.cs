namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 변형 풀 결정론 선택기 — 순수 정수 해시라 같은 (시드, 방 인덱스, 풀 크기)면 어디서든 같은 답이 나온다(AC-02).
    /// 블루프린트·해시 대조(X7)에는 관여하지 않는 파생 선택이며, 런타임 조립기와 에디터 프리뷰 빌더가
    /// 이 함수 하나를 공유해 선택 드리프트를 원천 차단한다(어셈블리 경계상 Core 가 유일한 공용 지점).
    /// </summary>
    public static class VariantSelector
    {
        /// <summary>시드·방 인덱스에서 변형 풀 인덱스를 뽑는다.</summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="roomIndex">블루프린트 방 인덱스.</param>
        /// <param name="variantCount">풀 크기.</param>
        /// <returns>선택 변형 인덱스(0 ≤ i &lt; variantCount).</returns>
        public static int RoomVariantIndex(int seed, int roomIndex, int variantCount)
        {
            if (variantCount <= 1)
            {
                return 0;
            }

            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)roomIndex * 2654435761u; // Knuth 곱셈 해시 — 인접 인덱스 상관 제거
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return (int)(h % (uint)variantCount);
            }
        }
    }
}
