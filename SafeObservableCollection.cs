using System.Collections.ObjectModel;

namespace DirectSFTP
{
   
    public class SafeObservableCollection<T>
    {
        private ObservableCollection<T> collection  = new();
        private readonly object lockObj = new ();
      
        public void Work(Action<ObservableCollection<T>> action)
        {   
            lock(lockObj)
            {
                action(collection);    
            }
        }

        public T Last()
        {
            lock (lockObj)
            {
                return collection[collection.Count - 1];
            }
        }
        

        public ObservableCollection<T> GetCollection()
        {
            return collection;
        }
    }
}
