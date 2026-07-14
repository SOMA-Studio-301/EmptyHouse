public interface IZombieState
{
    ZombieStateKind Kind { get; }
    void Enter(ZombieStateMachine machine);
    void Tick(ZombieStateMachine machine, float deltaTime);
    void Exit(ZombieStateMachine machine);
}