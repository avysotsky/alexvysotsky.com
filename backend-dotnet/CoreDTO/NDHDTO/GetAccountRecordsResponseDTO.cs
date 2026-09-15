using CoreDTO.NDHDTO;
using System.Collections.Generic;

public sealed class GetAccountRecordsResponseDTO
{
    public int accId { get; set; }
    public List<Record> records { get; set; } = new();
}

