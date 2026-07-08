namespace Border.Pool
{
    public interface IPool<T>
    {
        public void Prewarm(int num);
        public T Request();
        public void Return(T member);
    }
}
