using Unity.Netcode.Components;

/// <summary>
/// 소유자 권한으로 동작하는 NetworkAnimator. 이 프로젝트의 플레이어는 소유 클라이언트가
/// 이동·애니메이션을 구동하므로(서버 권한 아님), 소유자가 세팅한 파라미터가 원격에 복제되도록 재정의한다.
/// </summary>
public class OwnerNetworkAnimator : NetworkAnimator
{
    /// <summary>서버 권한이 아닌 소유자 권한으로 동작하도록 false 를 반환한다.</summary>
    /// <returns>항상 false — 소유자 권한.</returns>
    protected override bool OnIsServerAuthoritative() => false;
}
