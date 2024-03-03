// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using FellowOakDicom;
using FellowOakDicom.StructuredReport;
using Hl7.Fhir.Model;
using Microsoft.Health.DicomCast.Core.Features.Fhir;
using Microsoft.Health.DicomCast.Core.Features.Worker.FhirTransaction;
using Xunit;

namespace Microsoft.Health.DicomCast.Core.UnitTests.Features.Worker.FhirTransaction;

public class ObservationParserTests
{
    [Fact]
    public void RadiationEventWithAllSupportedAttributes()
    {
        const string randomIrradiationEventUid = "1.2.3.4.5.6.123123";
        const decimal randomDecimalNumber = (decimal)0.10;
        var randomRadiationMeasurementCodeItem = new DicomCodeItem("mGy", "UCUM", "mGy");
        var report = new DicomStructuredReport(
            ObservationConstants.IrradiationEventXRayData,
            new DicomContentItem(
                ObservationConstants.IrradiationEventUid,
                DicomRelationship.Contains,
                new DicomUID(randomIrradiationEventUid, "", DicomUidType.Unknown)),
            new DicomContentItem(
                ObservationConstants.MeanCtdIvol,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.Dlp,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.CtdIwPhantomType,
                DicomRelationship.Contains,
                new DicomCodeItem("113691", "DCM", "IEC Body Dosimetry Phantom")));

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            IdentifierUtility.CreateIdentifier(randomIrradiationEventUid));
        Assert.Single(observations);

        Observation radiationEvent = observations.First();
        Assert.Single(radiationEvent.Identifier);
        Assert.Equal("urn:oid:" + randomIrradiationEventUid, radiationEvent.Identifier[0].Value);
        Assert.Equal(2,
            radiationEvent.Component
                .Count(component => component.Value is Quantity));
        Assert.Equal(1,
            radiationEvent.Component
                .Count(component => component.Value is CodeableConcept));
    }

    [Fact]
    public void DoseSummaryWithAllSupportedAttributes()
    {
        const string studyInstanceUid = "1.3.12.2.123.5.4.5.123123.123123";
        const string accessionNumber = "random-accession";
        const decimal randomDecimalNumber = (decimal)0.10;
        var randomRadiationMeasurementCodeItem = new DicomCodeItem("mGy", "UCUM", "mGy");

        var report = new DicomStructuredReport(
            ObservationConstants.RadiopharmaceuticalRadiationDoseReport,
            // identifiers
            new DicomContentItem(
                ObservationConstants.StudyInstanceUid,
                DicomRelationship.HasProperties,
                new DicomUID(studyInstanceUid, "", DicomUidType.Unknown)),
            new DicomContentItem(
                ObservationConstants.AccessionNumber,
                DicomRelationship.HasProperties,
                DicomValueType.Text,
                accessionNumber),

            // attributes
            new DicomContentItem(
                ObservationConstants.DoseRpTotal,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.AccumulatedAverageGlandularDose,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.DoseAreaProductTotal,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.FluoroDoseAreaProductTotal,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.AcquisitionDoseAreaProductTotal,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.TotalFluoroTime,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.TotalNumberOfRadiographicFrames,
                DicomRelationship.Contains,
                new DicomMeasuredValue(10,
                    new DicomCodeItem("1", "UCUM", "No units"))),
            new DicomContentItem(
                ObservationConstants.AdministeredActivity,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.CtDoseLengthProductTotal,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.TotalNumberOfIrradiationEvents,
                DicomRelationship.Contains,
                new DicomMeasuredValue(10,
                    new DicomCodeItem("1", "UCUM", "No units"))),
            new DicomContentItem(
                ObservationConstants.MeanCtdIvol,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.RadiopharmaceuticalAgent,
                DicomRelationship.Contains,
                DicomValueType.Text,
                "Uranium"),
            new DicomContentItem(
                ObservationConstants.Radionuclide,
                DicomRelationship.Contains,
                DicomValueType.Text,
                "Uranium"),
            new DicomContentItem(
                ObservationConstants.RadiopharmaceuticalVolume,
                DicomRelationship.Contains,
                new DicomMeasuredValue(randomDecimalNumber,
                    randomRadiationMeasurementCodeItem)),
            new DicomContentItem(
                ObservationConstants.RouteOfAdministration,
                DicomRelationship.Contains,
                new DicomCodeItem("needle", "random-scheme", "this is made up"))
        );

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            IdentifierUtility.CreateIdentifier(studyInstanceUid));

        Assert.Single(observations);

        Observation doseSummary = observations.First();
        Assert.Equal(2, doseSummary.Identifier.Count);
        Assert.Equal("urn:oid:" + studyInstanceUid,
            doseSummary.Identifier[0].Value);
        Assert.Equal(accessionNumber,
            doseSummary.Identifier[1].Value);
        Assert.Equal(10,
            doseSummary.Component
                .Count(component => component.Value is Quantity));
        Assert.Equal(2,
            doseSummary.Component
                .Count(component => component.Value is Integer));
        Assert.Equal(2,
            doseSummary.Component
                .Count(component => component.Value is FhirString));
        Assert.Equal(1,
            doseSummary.Component
                .Count(component => component.Value is CodeableConcept));
    }

    [Fact]
    public void DoseSummaryWithStudyInstanceUidInTag()
    {
        var report = new DicomStructuredReport(
            ObservationConstants.XRayRadiationDoseReport);
        report.Dataset
            .Add(DicomTag.StudyInstanceUID, "12345")
            .Add(DicomTag.AccessionNumber, "12345");

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            new Identifier());
        Assert.Single(observations);
    }


    [Fact]
    public void DoseSummaryWithStudyInstanceUidInReport()
    {
        var report = new DicomStructuredReport(
            ObservationConstants.XRayRadiationDoseReport,
            new DicomContentItem(
                ObservationConstants.StudyInstanceUid,
                DicomRelationship.HasProperties,
                new DicomUID("1.3.12.2.123.5.4.5.123123.123123", "", DicomUidType.Unknown)));

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            new Identifier());
        Assert.Single(observations);
    }

    [Fact]
    public void RadiationEventWithIrradiationEventUid()
    {
        var report = new DicomStructuredReport(
            ObservationConstants.IrradiationEventXRayData,
            new DicomContentItem(
                ObservationConstants.IrradiationEventUid,
                DicomRelationship.Contains,
                new DicomUID("1.3.12.2.1234.5.4.5.123123.3000000111", "foobar", DicomUidType.Unknown)
            ));

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            new Identifier());
        Assert.Single(observations);
    }

    [Fact]
    public void RadiationEventWithoutIrradiationEventUid()
    {
        var report = new DicomStructuredReport(
            ObservationConstants.IrradiationEventXRayData);

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            new Identifier());
        Assert.Empty(observations);
    }

    [Fact]
    public void RadiationEventWithInvalidDataNotInCodeCodeNoErrorThrown()
    {
        var report = new DicomStructuredReport(
            ObservationConstants.IrradiationEventXRayData,
            new DicomContentItem(
                ObservationConstants.IrradiationEventUid,
                DicomRelationship.Contains,
                new DicomUID("1.3.12.2.1234.5.4.5.123123.3000000111", "foobar", DicomUidType.Unknown)
            ));
        report.Dataset.NotValidated();
        report.Dataset.Add(DicomTag.PatientBirthDateInAlternativeCalendar, "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz");

        var observations = ObservationParser.Parse(
            report.Dataset,
            new ResourceReference(),
            new ResourceReference(),
            new Identifier());
        Assert.Single(observations);
    }
}
