using System.Threading.Tasks;

namespace CoreDTO.HTTP
{
    public interface IPinger
    {
        public string ServerAddress { get;}
        public Task<bool> Ping();
    }
}
