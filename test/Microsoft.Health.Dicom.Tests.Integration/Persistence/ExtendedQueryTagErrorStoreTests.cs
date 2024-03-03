// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using FellowOakDicom;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.ExtendedQueryTag;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Features.Partitioning;
using Microsoft.Health.Dicom.Core.Features.Store;
using Microsoft.Health.Dicom.Core.Features.Validation;
using Microsoft.Health.Dicom.Tests.Common;
using Microsoft.Health.Dicom.Tests.Common.Extensions;
using Microsoft.Health.Dicom.Tests.Integration.Persistence.Models;
using Xunit;

namespace Microsoft.Health.Dicom.Tests.Integration.Persistence;

public class ExtendedQueryTagErrorStoreTests : IClassFixture<SqlDataStoreTestsFixture>, IAsyncLifetime
{
    private readonly IExtendedQueryTagErrorStore _extendedQueryTagErrorStore;
    private readonly IExtendedQueryTagStore _extendedQueryTagStore;
    private readonly IIndexDataStore _indexDataStore;
    private readonly IExtendedQueryTagErrorStoreTestHelper _errorStoreTestHelper;
    private readonly IExtendedQueryTagStoreTestHelper _tagStoreTestHelper;
    private readonly IIndexDataStoreTestHelper _indexDataStoreTestHelper;

    public ExtendedQueryTagErrorStoreTests(SqlDataStoreTestsFixture fixture)
    {
        EnsureArg.IsNotNull(fixture, nameof(fixture));
        _extendedQueryTagStore = EnsureArg.IsNotNull(fixture.ExtendedQueryTagStore, nameof(fixture.ExtendedQueryTagStore));
        _extendedQueryTagErrorStore = EnsureArg.IsNotNull(fixture.ExtendedQueryTagErrorStore, nameof(fixture.ExtendedQueryTagErrorStore));
        _tagStoreTestHelper = EnsureArg.IsNotNull(fixture.ExtendedQueryTagStoreTestHelper, nameof(fixture.ExtendedQueryTagStoreTestHelper));
        _indexDataStore = EnsureArg.IsNotNull(fixture.IndexDataStore, nameof(fixture.IndexDataStore));
        _errorStoreTestHelper = EnsureArg.IsNotNull(fixture.ExtendedQueryTagErrorStoreTestHelper, nameof(fixture.ExtendedQueryTagErrorStoreTestHelper));
        _indexDataStoreTestHelper = EnsureArg.IsNotNull(fixture.IndexDataStoreTestHelper, nameof(fixture.IndexDataStoreTestHelper));
    }

    public async Task DisposeAsync()
    {
        await _errorStoreTestHelper.ClearExtendedQueryTagErrorTableAsync();
        await _tagStoreTestHelper.ClearExtendedQueryTagTablesAsync();
        await _indexDataStoreTestHelper.ClearIndexTablesAsync();
        await _indexDataStoreTestHelper.ClearDeletedInstanceTableAsync();
    }

    [Fact]
    public async Task GivenMultipleErrors_WhenGettingPaginatedResults_ThenProperlyPaginateErrors()
    {
        // Add instances
        DicomTag tag = DicomTag.PatientName;
        long[] watermarks = new long[]
        {
            await AddInstanceAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate()),
            await AddInstanceAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate()),
            await AddInstanceAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate()),
            await AddInstanceAsync(TestUidGenerator.Generate(), TestUidGenerator.Generate(), TestUidGenerator.Generate()),
        };

        int tagKey = await AddTagAsync(tag);

        // Add multiple errors
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.PersonNameExceedMaxGroups, watermarks[0]);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.ExceedMaxLength, watermarks[1]);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.InvalidCharacters, watermarks[2]);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.PersonNameGroupExceedMaxLength, watermarks[3]);

        IReadOnlyList<ExtendedQueryTagError> errors;

        // Page 1
        errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 1, 0);
        Assert.Single(errors);
        Assert.Equal(errors[0].ErrorMessage, ValidationErrorCode.PersonNameExceedMaxGroups.GetMessage());

        // Page 2
        errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 2, 1);
        Assert.Equal(2, errors.Count);
        Assert.Equal(errors[0].ErrorMessage, ValidationErrorCode.ExceedMaxLength.GetMessage());
        Assert.Equal(errors[1].ErrorMessage, ValidationErrorCode.InvalidCharacters.GetMessage());

        // Page 3
        errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 1, 3);
        Assert.Single(errors);
        Assert.Equal(errors[0].ErrorMessage, ValidationErrorCode.PersonNameGroupExceedMaxLength.GetMessage());
    }

    [Fact]
    public async Task GivenValidExtendedQueryTagError_WhenAddExtendedQueryTagError_ThenTagErrorShouldBeAdded()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);
        ValidationErrorCode errorCode = ValidationErrorCode.ExceedMaxLength;

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(
           tagKey,
           errorCode,
           watermark);

        var errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 1, 0);

        Assert.Equal(errors[0].StudyInstanceUid, studyInstanceUid);
        Assert.Equal(errors[0].SeriesInstanceUid, seriesInstanceUid);
        Assert.Equal(errors[0].SopInstanceUid, sopInstanceUid);
        Assert.Equal(errors[0].ErrorMessage, errorCode.GetMessage());
    }

    [Fact]
    public async Task GivenNonExistingQueryTag_WhenAddExtendedQueryTagError_ThenShouldThrowException()
    {
        await Assert.ThrowsAsync<ExtendedQueryTagNotFoundException>(
            () => _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(int.MaxValue, ValidationErrorCode.InvalidCharacters, 1));
    }

    [Fact]
    public async Task GivenExistingExtendedQueryTagandTagError_WhenDeleteExtendedQueryTag_ThenTagErrorShouldAlsoBeRemoved()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);
        ValidationErrorCode errorCode = ValidationErrorCode.InvalidCharacters;
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark);

        var extendedQueryTagErrorBeforeTagDeletion = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), int.MaxValue, 0);
        Assert.Single(extendedQueryTagErrorBeforeTagDeletion);

        var extendedQueryTagBeforeTagDeletion = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());

        await _extendedQueryTagStore.DeleteExtendedQueryTagAsync(tag.GetPath(), tag.GetDefaultVR().Code);

        await Assert.ThrowsAsync<ExtendedQueryTagNotFoundException>(
            () => _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath()));
        await Assert.ThrowsAsync<ExtendedQueryTagNotFoundException>(
            () => _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 1, 0));
        Assert.False(await _errorStoreTestHelper.DoesExtendedQueryTagErrorExistAsync(tagKey));
    }

    [Fact]
    public async Task GivenExistingInstanceandExtendedQueryTagandTagError_WhenDeleteInstance_ThenTagErrorShouldAlsoBeRemoved()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);
        ValidationErrorCode errorCode = ValidationErrorCode.MultipleValues;
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark);
        var extendedQueryTagErrorBeforeTagDeletion = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), int.MaxValue, 0);
        Assert.Single(extendedQueryTagErrorBeforeTagDeletion);

        IReadOnlyList<Instance> instanceBeforeDeletion = await _indexDataStoreTestHelper.GetInstancesAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        Assert.Single(instanceBeforeDeletion);

        await _indexDataStore.DeleteInstanceIndexAsync(Partition.Default, studyInstanceUid, seriesInstanceUid, sopInstanceUid, DateTimeOffset.UtcNow);

        Assert.Empty(await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), 1, 0));
        Assert.False(await _errorStoreTestHelper.DoesExtendedQueryTagErrorExistAsync(tagKey));

        IReadOnlyList<Instance> instanceAfterDeletion = await _indexDataStoreTestHelper.GetInstancesAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        Assert.Empty(instanceAfterDeletion);
    }

    [Fact]
    public async Task GivenStudyWithMultileInstances_WhenDeleteStudy_ThenAssociatedErrorsShouldBeRemoved()
    {
        string studyUid1 = TestUidGenerator.Generate();
        string seriesUid1 = TestUidGenerator.Generate();
        string instanceUid1 = TestUidGenerator.Generate();
        string seriesUid2 = TestUidGenerator.Generate();
        string instanceUid2 = TestUidGenerator.Generate();
        string studyUid3 = TestUidGenerator.Generate();
        string seriesUid3 = TestUidGenerator.Generate();
        string instanceUid3 = TestUidGenerator.Generate();

        // add instances: instance1 and instance2 are in same study
        long watermark1 = await AddInstanceAsync(studyUid1, seriesUid1, instanceUid1);
        long watermark2 = await AddInstanceAsync(studyUid1, seriesUid2, instanceUid2);
        long watermark3 = await AddInstanceAsync(studyUid3, seriesUid3, instanceUid3);

        // add tag
        DicomTag tag = DicomTag.DeviceSerialNumber;
        int tagKey = await AddTagAsync(tag);


        // add errors
        ValidationErrorCode errorCode = ValidationErrorCode.DateIsInvalid;
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark1);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark2);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark3);

        // delete instance
        await _indexDataStore.DeleteStudyIndexAsync(Partition.Default, studyUid1, DateTime.UtcNow);

        // check errors
        var errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), int.MaxValue, 0);
        Assert.Single(errors);
        Assert.Equal(studyUid3, errors[0].StudyInstanceUid);
        Assert.Equal(seriesUid3, errors[0].SeriesInstanceUid);
        Assert.Equal(instanceUid3, errors[0].SopInstanceUid);
    }

    [Fact]
    public async Task GivenSeriesWithMultileInstances_WhenDeleteSeries_ThenAssociatedErrorsShouldBeRemoved()
    {
        string studyUid = TestUidGenerator.Generate();
        string seriesUid1 = TestUidGenerator.Generate();
        string instanceUid1 = TestUidGenerator.Generate();
        string instanceUid2 = TestUidGenerator.Generate();
        string seriesUid3 = TestUidGenerator.Generate();
        string instanceUid3 = TestUidGenerator.Generate();

        // add instances: instance1 and instance2 are in same series
        long watermark1 = await AddInstanceAsync(studyUid, seriesUid1, instanceUid1);
        long watermark2 = await AddInstanceAsync(studyUid, seriesUid1, instanceUid2);
        long watermark3 = await AddInstanceAsync(studyUid, seriesUid3, instanceUid3);

        // add tag
        DicomTag tag = DicomTag.DeviceSerialNumber;
        int tagKey = await AddTagAsync(tag);

        // add errors
        ValidationErrorCode errorCode = ValidationErrorCode.DateIsInvalid;
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark1);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark2);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark3);

        // delete instance
        await _indexDataStore.DeleteSeriesIndexAsync(Partition.Default, studyUid, seriesUid1, DateTime.UtcNow);

        // check errors
        var errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag.GetPath(), int.MaxValue, 0);
        Assert.Single(errors);
        Assert.Equal(studyUid, errors[0].StudyInstanceUid);
        Assert.Equal(seriesUid3, errors[0].SeriesInstanceUid);
        Assert.Equal(instanceUid3, errors[0].SopInstanceUid);
    }

    [Fact]
    public async Task GivenInstances_WhenDeleteInstance_ThenAssociatedErrorsShouldBeRemoved()
    {
        string studyUid1 = TestUidGenerator.Generate();
        string seriesUid1 = TestUidGenerator.Generate();
        string instanceUid1 = TestUidGenerator.Generate();
        string studyUid2 = TestUidGenerator.Generate();
        string seriesUid2 = TestUidGenerator.Generate();
        string instanceUid2 = TestUidGenerator.Generate();

        // add instances
        long watermark1 = await AddInstanceAsync(studyUid1, seriesUid1, instanceUid1);
        long watermark2 = await AddInstanceAsync(studyUid2, seriesUid2, instanceUid2);

        // add tag
        DicomTag tag1 = DicomTag.DeviceSerialNumber;
        int tagKey = await AddTagAsync(tag1);


        // add errors
        ValidationErrorCode errorCode = ValidationErrorCode.DateIsInvalid;
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark1);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, errorCode, watermark2);

        // delete instance
        await _indexDataStore.DeleteInstanceIndexAsync(new InstanceIdentifier(studyUid1, seriesUid1, instanceUid1, Partition.Default));

        // check errors
        var errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag1.GetPath(), int.MaxValue, 0);
        Assert.Single(errors);
        Assert.Equal(studyUid2, errors[0].StudyInstanceUid);
        Assert.Equal(seriesUid2, errors[0].SeriesInstanceUid);
        Assert.Equal(instanceUid2, errors[0].SopInstanceUid);
    }

    [Fact]
    public async Task GivenMultipleTagsOnSameInstance_WhenDeleteInstance_ThenAssociatedErrorsShouldBeRemoved()
    {
        string studyUid1 = TestUidGenerator.Generate();
        string seriesUid1 = TestUidGenerator.Generate();
        string instanceUid1 = TestUidGenerator.Generate();
        string studyUid2 = TestUidGenerator.Generate();
        string seriesUid2 = TestUidGenerator.Generate();
        string instanceUid2 = TestUidGenerator.Generate();

        // add instances
        long watermark1 = await AddInstanceAsync(studyUid1, seriesUid1, instanceUid1);
        long watermark2 = await AddInstanceAsync(studyUid2, seriesUid2, instanceUid2);

        // add tags
        DicomTag tag1 = DicomTag.DeviceSerialNumber;
        int tagKey1 = await AddTagAsync(tag1);
        DicomTag tag2 = DicomTag.DeviceID;
        int tagKey2 = await AddTagAsync(tag2);

        // add errors
        ValidationErrorCode errorCode = ValidationErrorCode.DateIsInvalid;

        // both tag has error on instance1
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey1, errorCode, watermark1);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey2, errorCode, watermark1);

        // Only tag2 has error on instance2
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey2, errorCode, watermark2);

        // delete instance1
        await _indexDataStore.DeleteInstanceIndexAsync(new InstanceIdentifier(studyUid1, seriesUid1, instanceUid1, Partition.Default));

        // check errors
        Assert.Empty(await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag1.GetPath(), 1, 0));
        var errors = await _extendedQueryTagErrorStore.GetExtendedQueryTagErrorsAsync(tag2.GetPath(), int.MaxValue, 0);
        Assert.Single(errors);
        Assert.Equal(studyUid2, errors[0].StudyInstanceUid);
        Assert.Equal(seriesUid2, errors[0].SeriesInstanceUid);
        Assert.Equal(instanceUid2, errors[0].SopInstanceUid);
    }
    [Fact]
    public async Task GivenTags_WhenDeleteTag_ThenAssociatedErrorsShouldBeRemoved()
    {
        string studyUid = TestUidGenerator.Generate();
        string seriesUid = TestUidGenerator.Generate();
        string instanceUid = TestUidGenerator.Generate();

        // add instances
        long watermark = await AddInstanceAsync(studyUid, seriesUid, instanceUid);

        // add tag
        DicomTag tag1 = DicomTag.DeviceSerialNumber;
        int tagKey1 = await AddTagAsync(tag1);
        DicomTag tag2 = DicomTag.DeviceID;
        int tagKey2 = await AddTagAsync(tag2);

        // add errors
        ValidationErrorCode errorCode = ValidationErrorCode.DateIsInvalid;
        // add error on instance for both tag
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey1, errorCode, watermark);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey2, errorCode, watermark);

        // delete tag
        await _extendedQueryTagStore.DeleteExtendedQueryTagAsync(tag1.GetPath(), tag1.GetDefaultVR().Code);

        // check errors
        Assert.False(await _errorStoreTestHelper.DoesExtendedQueryTagErrorExistAsync(tagKey1));
        Assert.True(await _errorStoreTestHelper.DoesExtendedQueryTagErrorExistAsync(tagKey2));
    }

    [Fact]
    public async Task GivenExtendedQueryTagError_WhenAddExtendedQueryTagError_ThenTagQueryStatusShouldBeDisabled()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(
            tagKey,
            ValidationErrorCode.UidIsInvalid,
            watermark);

        var tagEntry = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());
        Assert.Equal(QueryStatus.Disabled, tagEntry.QueryStatus);
    }

    [Fact]
    public async Task GivenExtendedQueryTagError_WhenAddExtendedQueryTagError_ThenErrorCountShouldIncrease()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(
            tagKey,
            ValidationErrorCode.UidIsInvalid,
            watermark);

        var tagEntry = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());
        Assert.Equal(1, tagEntry.ErrorCount);
    }

    [Fact]
    public async Task GivenExistingExtendedQueryTagError_WhenAddExtendedQueryTagError_ThenErrorCountShouldNotIncrease()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey = await AddTagAsync(tag);

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(
            tagKey,
            ValidationErrorCode.UidIsInvalid,
            watermark);

        // add on same instance again
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(
           tagKey,
           ValidationErrorCode.DateIsInvalid,
           watermark);

        var tagEntry = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());
        Assert.Equal(1, tagEntry.ErrorCount);
    }

    [Fact]
    public async Task GivenExtendedQueryTagErrorsOnMultipleTags_WhenDeleteAssociatedInstance_ThenErrorCountShouldDecrease()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid = TestUidGenerator.Generate();
        string sopInstanceUid = TestUidGenerator.Generate();

        DicomTag tag1 = DicomTag.DeviceSerialNumber;
        DicomTag tag2 = DicomTag.DeviceID;
        long watermark = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid, sopInstanceUid);
        int tagKey1 = await AddTagAsync(tag1);
        int tagKey2 = await AddTagAsync(tag2);

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey1, ValidationErrorCode.UidIsInvalid, watermark);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey2, ValidationErrorCode.UidIsInvalid, watermark);

        // Delete instance
        await _indexDataStore.DeleteInstanceIndexAsync(new InstanceIdentifier(studyInstanceUid, seriesInstanceUid, sopInstanceUid, Partition.Default));

        var tagEntry1 = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag1.GetPath());
        Assert.Equal(0, tagEntry1.ErrorCount);
        var tagEntry2 = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag2.GetPath());
        Assert.Equal(0, tagEntry2.ErrorCount);
    }

    [Fact]
    public async Task GivenExtendedQueryTagErrors_WhenDeleteAssociatedStudy_ThenErrorCountShouldDecrease()
    {
        string studyInstanceUid = TestUidGenerator.Generate();
        string seriesInstanceUid1 = TestUidGenerator.Generate();
        string sopInstanceUid1 = TestUidGenerator.Generate();
        string seriesInstanceUid2 = TestUidGenerator.Generate();
        string sopInstanceUid2 = TestUidGenerator.Generate();

        DicomTag tag = DicomTag.DeviceSerialNumber;
        long watermark1 = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid1, sopInstanceUid1);
        long watermark2 = await AddInstanceAsync(studyInstanceUid, seriesInstanceUid2, sopInstanceUid2);
        int tagKey = await AddTagAsync(tag);

        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.UidIsInvalid, watermark1);
        await _extendedQueryTagErrorStore.AddExtendedQueryTagErrorAsync(tagKey, ValidationErrorCode.UidIsInvalid, watermark2);

        // Before delete
        var tagEntryBefore = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());
        Assert.Equal(2, tagEntryBefore.ErrorCount);

        // Delete study
        await _indexDataStore.DeleteStudyIndexAsync(Partition.Default, studyInstanceUid, DateTimeOffset.UtcNow);

        var tagEntryAfter = await _extendedQueryTagStore.GetExtendedQueryTagAsync(tag.GetPath());
        Assert.Equal(0, tagEntryAfter.ErrorCount);
    }


    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    private async Task<long> AddInstanceAsync(string studyId, string seriesId, string sopInstanceId)
    {
        DicomDataset dataset = Samples.CreateRandomInstanceDataset(studyId, seriesId, sopInstanceId);
        long watermark = await _indexDataStore.BeginCreateInstanceIndexAsync(new Partition(1, "clinic-one"), dataset);
        await _indexDataStore.EndCreateInstanceIndexAsync(1, dataset, watermark);
        return watermark;
    }

    private async Task<int> AddTagAsync(DicomTag tag)
    {
        AddExtendedQueryTagEntry extendedQueryTagEntry = tag.BuildAddExtendedQueryTagEntry();
        var list = await _extendedQueryTagStore.AddExtendedQueryTagsAsync(new AddExtendedQueryTagEntry[] { extendedQueryTagEntry }, 128);
        return list[0].Key;
    }
}
