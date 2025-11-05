using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public sealed class ReportesCloudService
    {
        private readonly ServiceClient _svc;
        private readonly ClientesService _clientesService;

        public ReportesCloudService(ServiceClient svc, ClientesService clientesService)
        {
            _svc = svc;
            _clientesService = clientesService;
        }

        public async Task<List<ReporteCloudDto>> GetReportesByUserEmailAsync(string email)
        {
            var clienteId = await _clientesService.GetClienteIdByEmailAsync(email);
            if (clienteId == Guid.Empty)
                return new List<ReporteCloudDto>();

            var q = new QueryExpression("cr07a_reportescloud")
            {
                ColumnSet = new ColumnSet("cr07a_tituloreporteseguridad", "cr07a_fecha", "cr07a_adjunto"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("cr07a_cliente", ConditionOperator.Equal, clienteId)
                    }
                },
                Orders =
                {
                    new OrderExpression("cr07a_fecha", OrderType.Descending)
                }
            };

            var results = _svc.RetrieveMultiple(q);
            var list = new List<ReporteCloudDto>();

            foreach (var e in results.Entities)
            {
                list.Add(new ReporteCloudDto
                {
                    Id = e.Id,
                    Titulo = e.GetAttributeValue<string>("cr07a_tituloreporteseguridad") ?? "(Sin título)",
                    Fecha = e.GetAttributeValue<DateTime?>("cr07a_fecha") ?? DateTime.MinValue,
                    AdjuntoId = e.GetAttributeValue<Guid?>("cr07a_adjunto") ?? Guid.Empty
                });
            }

            return list;
        }

        public (byte[] FileBytes, string FileName)? DescargarAdjunto(Guid reporteId)
        {
            // 1. Inicializar descarga
            var initReq = new InitializeFileBlocksDownloadRequest
            {
                Target = new EntityReference("cr07a_reportescloud", reporteId),
                FileAttributeName = "cr07a_adjunto"
            };

            var initResp = (InitializeFileBlocksDownloadResponse)_svc.Execute(initReq);
            if (initResp == null || string.IsNullOrEmpty(initResp.FileName))
                return null;

            var fileName = initResp.FileName;
            var fileSize = initResp.FileSizeInBytes;
            var token = initResp.FileContinuationToken;

            // 2. Descargar en bloques
            var fileBytes = new List<byte>();
            long offset = 0;
            const int blockSize = 4 * 1024 * 1024; // 4 MB

            while (offset < fileSize)
            {
                var blockReq = new DownloadBlockRequest
                {
                    FileContinuationToken = token,
                    BlockLength = blockSize,
                    Offset = offset
                };

                var blockResp = (DownloadBlockResponse)_svc.Execute(blockReq);
                if (blockResp.Data != null && blockResp.Data.Length > 0)
                {
                    fileBytes.AddRange(blockResp.Data);
                    offset += blockResp.Data.Length;
                }
                else
                {
                    break;
                }
            }

            return (fileBytes.ToArray(), fileName);
        }
    }
}