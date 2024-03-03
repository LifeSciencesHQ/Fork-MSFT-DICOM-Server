// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Dicom.Api.Features.Filters;
using Microsoft.Health.Dicom.Api.Features.Routing;
using Microsoft.Health.Dicom.Core.Configs;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Features.Context;
using Microsoft.Health.Dicom.Core.Features.Partitioning;
using Microsoft.Health.Dicom.Core.Messages.Partitioning;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Dicom.Api.UnitTests.Features.Filters;

public class PopulateDataPartitionFilterAttributeTests
{
    private readonly ControllerActionDescriptor _controllerActionDescriptor;
    private readonly HttpContext _httpContext;
    private readonly ActionExecutingContext _actionExecutingContext;
    private readonly IDicomRequestContextAccessor _dicomRequestContextAccessor;
    private readonly IMediator _mediator;
    private readonly IOptions<FeatureConfiguration> _featureConfiguration;
    private readonly ActionExecutionDelegate _nextActionDelegate;

    private const string ControllerName = "controller";
    private const string ActionName = "actionName";
    private const string RouteName = "routeName";

    private PopulateDataPartitionFilterAttribute _filterAttribute;

    public PopulateDataPartitionFilterAttributeTests()
    {
        _controllerActionDescriptor = new ControllerActionDescriptor
        {
            DisplayName = "Executing Context Test Descriptor",
            ActionName = ActionName,
            ControllerName = ControllerName,
            AttributeRouteInfo = new AttributeRouteInfo
            {
                Name = RouteName,
            },
        };

        _httpContext = Substitute.For<HttpContext>();

        _actionExecutingContext = new ActionExecutingContext(
            new ActionContext(_httpContext, new RouteData(), _controllerActionDescriptor),
            new List<IFilterMetadata>(),
            new Dictionary<string, object>(),
            FilterTestsHelper.CreateMockRetrieveController());

        var routeValueDictionary = new RouteValueDictionary
        {
            { KnownActionParameterNames.StudyInstanceUid, "123" },
            { KnownActionParameterNames.PartitionName, Partition.DefaultName },
        };
        _actionExecutingContext.RouteData = new RouteData(routeValueDictionary);

        _nextActionDelegate = Substitute.For<ActionExecutionDelegate>();

        _dicomRequestContextAccessor = Substitute.For<IDicomRequestContextAccessor>();

        _mediator = Substitute.For<IMediator>();
        _mediator.Send(Arg.Any<GetOrAddPartitionRequest>())
            .Returns(new GetOrAddPartitionResponse(Partition.Default));

        _mediator.Send(Arg.Any<GetPartitionRequest>())
            .Returns(new GetPartitionResponse(Partition.Default));

        _featureConfiguration = Options.Create(new FeatureConfiguration { EnableDataPartitions = true });

        _filterAttribute = new PopulateDataPartitionFilterAttribute(_dicomRequestContextAccessor, _mediator, _featureConfiguration);
    }

    [Fact]
    public Task GivenRetrieveRequestWithDataPartitionsEnabled_WhenNoPartitionId_ThenItShouldThrowError()
    {
        var routeValueDictionary = new RouteValueDictionary
        {
            { KnownActionParameterNames.StudyInstanceUid, "123" },
        };
        _actionExecutingContext.RouteData = new RouteData(routeValueDictionary);

        return Assert.ThrowsAsync<DataPartitionsMissingPartitionException>(() => _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, _nextActionDelegate));
    }

    [Fact]
    public Task GivenRetrieveRequestWithDataPartitionsDisabled_WhenPartitionIdIsPassed_ThenItShouldThrowError()
    {
        var routeValueDictionary = new RouteValueDictionary
        {
            { KnownActionParameterNames.StudyInstanceUid, "123" },
            { KnownActionParameterNames.PartitionName, "partition1" },
        };
        _actionExecutingContext.RouteData = new RouteData(routeValueDictionary);

        _featureConfiguration.Value.EnableDataPartitions = false;
        _filterAttribute = new PopulateDataPartitionFilterAttribute(_dicomRequestContextAccessor, _mediator, _featureConfiguration);

        return Assert.ThrowsAsync<DataPartitionsFeatureDisabledException>(() => _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, _nextActionDelegate));
    }

    [Fact]
    public async Task GivenRetrieveRequestWithDataPartitionsDisabled_WhenNoPartitionId_ThenItExecutesSuccessfully()
    {
        var routeValueDictionary = new RouteValueDictionary
        {
            { KnownActionParameterNames.StudyInstanceUid, "123" },
        };
        _actionExecutingContext.RouteData = new RouteData(routeValueDictionary);

        _featureConfiguration.Value.EnableDataPartitions = false;
        _filterAttribute = new PopulateDataPartitionFilterAttribute(_dicomRequestContextAccessor, _mediator, _featureConfiguration);

        await _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, _nextActionDelegate);
    }

    [Fact]
    public async Task GivenExistingPartitionNamePassed_ThenContextShouldBeSet()
    {
        await _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, Substitute.For<ActionExecutionDelegate>());

        _dicomRequestContextAccessor.Received().RequestContext.DataPartition = Partition.Default;
    }

    [Fact]
    public async Task GivenNonExistingPartitionNamePassed_AndStowRequest_ThenPartitionIsCreated()
    {
        var newPartitionKey = 3;
        var newPartitionName = "partition";
        var newPartition = new Partition(newPartitionKey, newPartitionName);

        _controllerActionDescriptor.AttributeRouteInfo.Name = KnownRouteNames.PartitionStoreInstance;

        _mediator.Send(Arg.Any<GetOrAddPartitionRequest>())
            .Returns(new GetOrAddPartitionResponse(null));

        await _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, _nextActionDelegate);

        _dicomRequestContextAccessor.Received().RequestContext.DataPartition = newPartition;
    }

    [Fact]
    public async Task GivenNonExistingPartitionNamePassed_AndAddWorkitemRequest_ThenPartitionIsCreated()
    {
        var newPartitionKey = 3;
        var newPartitionName = "partition";
        var newPartition = new Partition(newPartitionKey, newPartitionName);

        _controllerActionDescriptor.AttributeRouteInfo.Name = KnownRouteNames.PartitionedAddWorkitemInstance;

        _mediator.Send(Arg.Any<GetOrAddPartitionRequest>())
            .Returns(new GetOrAddPartitionResponse(null));

        await _filterAttribute.OnActionExecutionAsync(_actionExecutingContext, _nextActionDelegate);

        _dicomRequestContextAccessor.Received().RequestContext.DataPartition = newPartition;
    }
}
