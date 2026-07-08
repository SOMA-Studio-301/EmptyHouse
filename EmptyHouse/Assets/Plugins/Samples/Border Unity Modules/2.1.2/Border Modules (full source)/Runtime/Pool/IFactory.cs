namespace Border.Pool
{
    public interface IFactory<T>
    {
        public T Create();
    }
}
