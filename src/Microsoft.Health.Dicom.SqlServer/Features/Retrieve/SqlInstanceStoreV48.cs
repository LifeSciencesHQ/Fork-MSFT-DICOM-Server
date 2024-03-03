// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Features.Common;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Features.Partitioning;
using Microsoft.Health.Dicom.Core.Models;
using Microsoft.Health.Dicom.SqlServer.Extensions;
using Microsoft.Health.Dicom.SqlServer.Features.Schema;
using Microsoft.Health.Dicom.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Dicom.SqlServer.Features.Retrieve;

internal class SqlInstanceStoreV48 : SqlInstanceStoreV1
{
    public SqlInstanceStoreV48(SqlConnectionWrapperFactory sqlConnectionWrapperFactory) : base(sqlConnectionWrapperFactory)
    {
    }

    public override SchemaVersion Version => SchemaVersion.V48;

    public override async Task<IReadOnlyList<WatermarkRange>> GetInstanceBatchesAsync(
        int batchSize,
        int batchCount,
        IndexStatus indexStatus,
        long? maxWatermark = null,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsGt(batchSize, 0, nameof(batchSize));
        EnsureArg.IsGt(batchCount, 0, nameof(batchCount));

        using SqlConnectionWrapper sqlConnectionWrapper = await SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken);
        using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

        VLatest.GetInstanceBatches.PopulateCommand(sqlCommandWrapper, batchSize, batchCount, (byte)indexStatus, maxWatermark);

        try
        {
            var batches = new List<WatermarkRange>();
            using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                batches.Add(new WatermarkRange(reader.GetInt64(0), reader.GetInt64(1)));
            }

            return batches;
        }
        catch (SqlException ex)
        {
            throw new DataStoreException(ex);
        }
    }

    public override Task<IReadOnlyList<VersionedInstanceIdentifier>> GetInstanceIdentifierAsync(
        Partition partition,
        string studyInstanceUid,
        string seriesInstanceUid,
        string sopInstanceUid,
        CancellationToken cancellationToken)
    {
        return GetInstanceIdentifierImp(partition, studyInstanceUid, cancellationToken, seriesInstanceUid, sopInstanceUid);
    }

    public override Task<IReadOnlyList<VersionedInstanceIdentifier>> GetInstanceIdentifiersInSeriesAsync(
        Partition partition,
        string studyInstanceUid,
        string seriesInstanceUid,
        CancellationToken cancellationToken)
    {
        return GetInstanceIdentifierImp(partition, studyInstanceUid, cancellationToken, seriesInstanceUid);
    }

    public override Task<IReadOnlyList<VersionedInstanceIdentifier>> GetInstanceIdentifiersInStudyAsync(
        Partition partition,
        string studyInstanceUid,
        CancellationToken cancellationToken)
    {
        return GetInstanceIdentifierImp(partition, studyInstanceUid, cancellationToken);
    }

    public override async Task<IReadOnlyList<VersionedInstanceIdentifier>> GetInstanceIdentifiersByWatermarkRangeAsync(
        WatermarkRange watermarkRange,
        IndexStatus indexStatus,
        CancellationToken cancellationToken = default)
    {
        var results = new List<VersionedInstanceIdentifier>();

        try
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetInstancesByWatermarkRangeV6.PopulateCommand(
                    sqlCommandWrapper,
                    watermarkRange.Start,
                    watermarkRange.End,
                    (byte)indexStatus);

                using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string rStudyInstanceUid, string rSeriesInstanceUid, string rSopInstanceUid, long watermark) = reader.ReadRow(
                           VLatest.Instance.StudyInstanceUid,
                           VLatest.Instance.SeriesInstanceUid,
                           VLatest.Instance.SopInstanceUid,
                           VLatest.Instance.Watermark);

                        results.Add(new VersionedInstanceIdentifier(
                            rStudyInstanceUid,
                            rSeriesInstanceUid,
                            rSopInstanceUid,
                            watermark));
                    }
                }
            }

        }
        catch (SqlException ex)
        {
            throw new DataStoreException(ex);
        }

        return results;
    }

    private async Task<IReadOnlyList<VersionedInstanceIdentifier>> GetInstanceIdentifierImp(
        Partition partition,
        string studyInstanceUid,
        CancellationToken cancellationToken,
        string seriesInstanceUid = null,
        string sopInstanceUid = null)
    {
        var results = new List<VersionedInstanceIdentifier>();

        try
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetInstanceV6.PopulateCommand(
                    sqlCommandWrapper,
                    validStatus: (byte)IndexStatus.Created,
                    partition.Key,
                    studyInstanceUid,
                    seriesInstanceUid,
                    sopInstanceUid);

                using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string rStudyInstanceUid, string rSeriesInstanceUid, string rSopInstanceUid, long watermark) = reader.ReadRow(
                           VLatest.Instance.StudyInstanceUid,
                           VLatest.Instance.SeriesInstanceUid,
                           VLatest.Instance.SopInstanceUid,
                           VLatest.Instance.Watermark);

                        results.Add(new VersionedInstanceIdentifier(
                                rStudyInstanceUid,
                                rSeriesInstanceUid,
                                rSopInstanceUid,
                                watermark,
                                partition));
                    }
                }
            }

            return results;
        }
        catch (SqlException ex)
        {
            throw new DataStoreException(ex);
        }
    }

    public override async Task<IReadOnlyList<InstanceMetadata>> GetInstanceIdentifierWithPropertiesAsync(Partition partition, string studyInstanceUid, string seriesInstanceUid = null, string sopInstanceUid = null, bool isInitialVersion = false, CancellationToken cancellationToken = default)
    {
        var results = new List<InstanceMetadata>();

        try
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetInstanceWithPropertiesV46.PopulateCommand(
                    sqlCommandWrapper,
                    validStatus: (byte)IndexStatus.Created,
                    partition.Key,
                    studyInstanceUid,
                    seriesInstanceUid,
                    sopInstanceUid);

                using (var reader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string rStudyInstanceUid,
                            string rSeriesInstanceUid,
                            string rSopInstanceUid,
                            long watermark,
                            string rTransferSyntaxUid,
                            bool rHasFrameMetadata,
                            long? originalWatermark,
                            long? newWatermark,
                            string filePath,
                            string eTag) = reader.ReadRow(
                              VLatest.Instance.StudyInstanceUid,
                              VLatest.Instance.SeriesInstanceUid,
                              VLatest.Instance.SopInstanceUid,
                              VLatest.Instance.Watermark,
                              VLatest.Instance.TransferSyntaxUid,
                              VLatest.Instance.HasFrameMetadata,
                              VLatest.Instance.OriginalWatermark,
                              VLatest.Instance.NewWatermark,
                              VLatest.FileProperty.FilePath.AsNullable(),
                              VLatest.FileProperty.ETag.AsNullable());

                        results.Add(
                            new InstanceMetadata(
                                new VersionedInstanceIdentifier(
                                    rStudyInstanceUid,
                                    rSeriesInstanceUid,
                                    rSopInstanceUid,
                                    watermark,
                                    partition),
                                new InstanceProperties()
                                {
                                    TransferSyntaxUid = rTransferSyntaxUid,
                                    HasFrameMetadata = rHasFrameMetadata,
                                    OriginalVersion = originalWatermark,
                                    NewVersion = newWatermark,
                                    FileProperties = string.IsNullOrEmpty(eTag) || string.IsNullOrEmpty(filePath)
                                        ? null
                                        : new FileProperties { ETag = eTag, Path = filePath }
                                }));
                    }
                }
            }
        }
        catch (SqlException ex)
        {
            throw new DataStoreException(ex);
        }

        return results;
    }

    public override async Task<IReadOnlyList<WatermarkRange>> GetInstanceBatchesByTimeStampAsync(
        int batchSize,
        int batchCount,
        IndexStatus indexStatus,
        DateTimeOffset startTimeStamp,
        DateTimeOffset endTimeStamp,
        long? maxWatermark = null,
        CancellationToken cancellationToken = default)
    {
        EnsureArg.IsGt(batchSize, 0, nameof(batchSize));
        EnsureArg.IsGt(batchCount, 0, nameof(batchCount));

        using SqlConnectionWrapper sqlConnectionWrapper = await SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken);
        using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

        VLatest.GetInstanceBatchesByTimeStamp.PopulateCommand(sqlCommandWrapper, batchSize, batchCount, (byte)indexStatus, startTimeStamp, endTimeStamp, maxWatermark);

        try
        {
            var batches = new List<WatermarkRange>();
            using SqlDataReader reader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                batches.Add(new WatermarkRange(reader.GetInt64(0), reader.GetInt64(1)));
            }

            return batches;
        }
        catch (SqlException ex)
        {
            throw new DataStoreException(ex);
        }
    }
}
