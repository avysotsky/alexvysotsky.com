using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{
    public class UserAccountsResponseDTO
    {
        public List<UserAccountListItemDTO> accounts { get; set; } = new();
    }
}
