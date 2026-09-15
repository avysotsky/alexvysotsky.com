using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{
    public class GetListOfAccountsResponseDTO
    {
        public List<Account> listOfAccounts { get; set; } = new();
    }
}