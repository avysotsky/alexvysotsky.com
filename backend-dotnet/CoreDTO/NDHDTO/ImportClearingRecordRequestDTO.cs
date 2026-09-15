using System;
using System.Collections.Generic;

namespace CoreDTO.NDHDTO
{
    public sealed class ImportClearingRecordRequestDTO
    {
        public int AccountId { get; set; }
        public long CidId { get; set; }
        public DateTime DtUtc { get; set; }
        public double EquityUsd { get; set; }
        public string Type { get; set; } = string.Empty;
        public double NewTotallyInvestedUsd { get; set; }
        public List<ImportClearingRecordCidDTO> Cids { get; set; } = new();
    }
}