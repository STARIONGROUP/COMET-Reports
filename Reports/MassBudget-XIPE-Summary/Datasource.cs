// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DataSource.cs" company=" System S.A.">
//    Copyright (c) 2015-2021  System S.A.
//
//    Author: Alexander van Delft, Sam Gerené, Alex Vorobiev
//
//    This file is part the COMET-Reports repository
//
//    The COMET-Reports are free software; you can redistribute it and/or
//    modify it under the terms of the GNU Lesser General Public
//    License as published by the Free Software Foundation; either
//    version 3 of the License, or any later version.
//
//    The COMET-Reports are distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//    Lesser General Public License for more details.
//
//    You should have received a copy of the GNU Lesser General Public License
//    along with this program; if not, write to the Free Software Foundation,
//    Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

/// <summary>
/// the using statements needed for all code to work
/// </summary>
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

using CDP4Reporting.DataCollection;
using CDP4Reporting.Parameters;
using CDP4Reporting.Utilities;

using CDP4Composition.Navigation;

using CDP4Common.EngineeringModelData;
using CDP4Common.SiteDirectoryData;
using CDP4Common.Helpers;

/// <summary>
/// A general static helper class
/// </summary>
public static class Variables
{
	/// <summary>
    /// The name of the Segment we want to get data for.
    /// </summary>
	public const string SpaceSegmentName = "Space Segment";

	// Paths to specific parameter values
	public static string LauncherAdapterPath = @"XIPE.Launch_Seg.VEGA.PLA_1194\m\\";
	public static string FuelMassPath = "";
	public static string OxidizerMassPath = "";
	public static string PressurantMassPath = "";
	public static string PropellantMassPath = "";

	/// <summary>
	/// This is a list of default SubSystems (Owners/DomainOfExpertises) where a report parameter should be created for.
    /// A report parameter used to switch between Product and Function level will always be available for these Owners/DomainOfExpertises.
	/// Add/Remove owner shortnames at your convenience
	/// </summary>
	public static List<string> SubsystemNames = new List<string>
    {
    	"AOGNC",
    	"COM",
    	"CPROP",
    	"DH",
    	"EPROP",
    	"INS",
    	"MEC",
    	"PWR",
    	"RAD",
    	"STR",
    	"SYE",
    	"TC"
	};

	// A list of NestedParameters to be used in this report
	public static List<NestedParameter> NestedParameters;

	// A list of Nestedelements to be used in this report
	public static List<NestedElement> NestedElements;

	// Initialize the Variables using an Option.
	public static void SetVariables(Option option)
	{
		if (option != null)
		{
			LauncherAdapterPath = ReportingUtilities.ConvertToOptionPath(LauncherAdapterPath, option);
			FuelMassPath = ReportingUtilities.ConvertToOptionPath(FuelMassPath, option);
			OxidizerMassPath = ReportingUtilities.ConvertToOptionPath(OxidizerMassPath, option);
			PressurantMassPath = ReportingUtilities.ConvertToOptionPath(PressurantMassPath, option);
			PropellantMassPath = ReportingUtilities.ConvertToOptionPath(PropellantMassPath, option);

			// Get the tree of NestedElements for the selected Option.
			NestedElements = new NestedElementTreeGenerator().Generate(option).ToList();

			// Get all contained NestedParameters from the tree of NestedElements for the selected Option
			NestedParameters = new NestedElementTreeGenerator().GetNestedParameters(option).ToList();
		}
	}
}

/// <summary>
/// Class that defines in the actual data source
/// Exactly one class that implements IDataCollector should be available in the code editor.
/// </summary>
public class MyDataSource : OptionDependentDataCollector
{
	/// <summary>
	/// A must override method that returns the actual data object.
	/// A data object could be anything, except a dynamic/ExpandoObject type.
	/// </summary>
	/// <returns>
	/// The data as an object.
	/// </returns>
	public override object CreateDataObject()
	{
		// Select an Option from the current Iteration.
		// this.SelectOption is a method of the OptionDependentDataCollector class
		// After selection the SelectedOption property will be set.
		this.SelectOption();
		var option = this.SelectedOption;

		Variables.SetVariables(option);

		// Create a CategoryDecompositionHierarchy instance that reads all elements in the ProductTree that
		// comply to the Hierarchy of categories defined here:
		//
		// - Missions
		//   | Segments
		//     | Systems [1..5 nesting levels]
		//       | Subsystems
		//
        // In case there are multiple nested Equipment levels in the model, the deepest level is selected
        // as the source for the parameter values.
		// The fieldnames in the result DataSource are set explicitly (second parameter of AddLevel method).
		var functionHierarchy = new CategoryDecompositionHierarchy
	        .Builder(this.Iteration)
			.AddLevel("Missions")
			.AddLevel("Segments")
			.AddLevel("Systems", "SystemName", 5)
			.AddLevel("Subsystems", "SubsystemName")
	        .Build();

		// Create a CategoryDecompositionHierarchy instance that reads all elements in the ProductTree that
		// comply to the Hierarchy of categories defined here:
		//
		// - Missions
		//   | Segments
		//     | Systems [1..5 nesting levels]
		//       | Subsystems
		//
        // In case there are multiple nested Elements levels in the model, the deepest level is selected
        // as the source for the parameter values.
		// The fieldnames in the result DataSource are set explicitly (second parameter of AddLevel method).
		var productHierarchy = new CategoryDecompositionHierarchy
	        .Builder(this.Iteration)
			.AddLevel("Missions")
			.AddLevel("Segments")
			.AddLevel("Elements", "SystemName", 5)
	        .AddLevel("Equipment", "SubsystemName")
	        .Build();

		// Build a DataTable for the productHierarchy level (Product level)
	    var resultDataSource =
	        new DataCollectorNodesCreator<RowRepresentation>()
	        	.GetTable(productHierarchy, Variables.NestedElements);

		// Build a DataTable for the functionHierarchy level (Function level)
	    var secondDataSource =
	        new DataCollectorNodesCreator<RowRepresentation>()
	        	.GetTable(functionHierarchy, Variables.NestedElements);

		// Merge the two datatables
		resultDataSource.Merge(secondDataSource);

		// Create a DataView that contains all Distinct values in the resultDataSource's Segments column,
		var segmentsTable = new DataView(resultDataSource).ToTable(true, "Segments");

		// Create the DataSet that will be returned by this CreateDataObject method.
		var dataSet = new DataSet();

		resultDataSource.Columns.Add("MassWithoutExtraMargin", typeof(double));

		// Set Extra Mass Margin data if found on the second Element level.
		foreach (DataRow dataRow in resultDataSource.Rows)
		{
			dataRow["MassWithoutExtraMargin"] = dataRow["Mass"];

			if (resultDataSource.Columns.Contains("SystemName_2_MassMargin"))
			{
				if ((bool)dataRow["Products"] == true
					&& dataRow["SystemName_2_MassMargin"] != DBNull.Value
					&& (double)dataRow["SystemName_2_MassMargin"] != 0D)
				{
					dataRow["HasExtraMassMargin"] = true;
					dataRow["ExtraMassMargin"] = (double)dataRow["SystemName_2_MassMargin"] / 100;
					dataRow["Mass"] = (double)dataRow["Mass"] * (1D + (double)dataRow["ExtraMassMargin"]);
				}
			}
		}

		// Find the data rows that contain a Segments name that is equal to the SpaceSegmentName set in the top of this file
		// and add that table to the DataSet.
		foreach (DataRow dataRow in segmentsTable.Rows)
		{
			var segment = dataRow["Segments"].ToString();

			if ((segment) == Variables.SpaceSegmentName)
			{
				var newView = new DataView(resultDataSource);
				newView.RowFilter = "Segments = '" + segment + "'";
				newView.Sort = "SystemName_1 ASC, SystemName_2 ASC, OwnerShortName ASC, SubsystemName ASC";
				var newTable = newView.ToTable();
				newTable.TableName = "MainData";
				dataSet.Tables.Add(newTable);
			}
		}

		return dataSet;
	}
}

/// <summary>
/// Class that defines the row representation.
/// Every property that has a public getter will be used in the result datasource
/// </summary>
public class RowRepresentation : DataCollectorRow
{
	/// <summary>
	/// The Parameter classes.
	/// Need to be public.
	/// </summary>
	[DefinedThingShortName("m", "MassWithoutMargin")]
	public DataCollectorDoubleParameter<RowRepresentation> parameterMass { get; set; }

	[DefinedThingShortName("mass_margin", "MassMargin")]
	[CollectParentValues]
	public DataCollectorDoubleParameter<RowRepresentation> parameterMassMargin { get; set; }

	[DefinedThingShortName("n_items")]
	public DataCollectorDoubleParameter<RowRepresentation> parameterNumberOfItems { get; set; }

	/// <summary>
	/// The Category classes.
	/// Need to be public.
	/// </summary>
	[DefinedThingShortName("Functions")]
	public DataCollectorCategory<RowRepresentation> functionsCategory { get; set; }

	[DefinedThingShortName("Products")]
	public DataCollectorCategory<RowRepresentation> productsCategory { get; set; }

	// Gets the number of items and returns 1 if it is 0
	public double NumberOfItems
	{
		get { return this.parameterNumberOfItems.Value == 0D ? 1D : this.parameterNumberOfItems.Value; }
	}

	/// <summary>
	/// The implementation of the Mass property/column in the result datasource.
    /// Includes the calculated margin.
	/// </summary>
	public double Mass
	{
	    get { return this.parameterMass.Value * (100 + this.parameterMassMargin.Value) / 100; }
	}

	/// <summary>
	/// The implementation of the OwnerShortName property/column in the result datasource.
	/// this.ElementBaseOwner is a default property of the abstract DataCollectorRow class,
	/// of which this class is derived from.
	/// this.ElementBaseOwner is the Owner DomainOfExpertise of the this.ElementBase property
	/// </summary>
	public string OwnerShortName
	{
	    get { return this.ElementBaseOwner.ShortName; }
	}

	// The ExtraMassMargin percentage if found on the second element level
	// Is set after DataTable creation.
	public double ExtraMassMargin
	{
		get { return 0D; }
	}

	// Indicates whether an Extra Mass margin is set at the second element level
	// Is set after DataTable creation.
	public bool HasExtraMassMargin
	{
		get { return false; }
	}

	/// <summary>
	/// The implementation of the OwnerName property/column in the result datasource.
	/// this.ElementBaseOwner is a default property of the abstract DataCollectorRow class,
	/// of which this class is derived from.
	/// this.ElementBaseOwner is the Owner DomainOfExpertise of the this.ElementBase property
	/// </summary>
	public string OwnerName
	{
	    get { return this.ElementBaseOwner.Name; }
	}

	/// <summary>
	/// Checks if this is Function or Product related data.
	/// This column is used for checking the Reporting Parameters (Product/Function/Not used)
	/// </summary>
	public string ProductFunction
	{
		get { return functionsCategory.Value ? "Function" : (productsCategory.Value ? "Product" : ""); }
	}
}

/// <summary>
/// A class that is used to build Report Parameters and optional a specific filter string at the
/// report level.
/// </summary>
public class MyParameters : ReportingParameters
{
	/// <summary>
	/// Creates a list of report reporting parameter that should dynamically be added to the
	/// Report Designer's report parameter list.
	/// </summary>
	public override IEnumerable<IReportingParameter> CreateParameters(object dataSource, IDataCollector dataCollector) {
	    var list = new List<IReportingParameter>();

	    var optionDependentDataCollector = dataCollector as IOptionDependentDataCollector;

		// Get the selected option.
		var option = optionDependentDataCollector.SelectedOption;

		// Get the selected options name.
		var optionName = option.Name;

		// Create a dynamic parameter for use in the report header
		var optionNameParameter = new ReportingParameter(
					"OptionName",
					typeof(string),
					optionName);
		optionNameParameter.Visible = false;
		list.Add(optionNameParameter);

		// Get the launcher mass using its Path property from the ProductTree.
		var launcherAdapterMass = string.IsNullOrWhiteSpace(Variables.LauncherAdapterPath) ? 0D : option.GetNestedParameterValuesByPath<double>(
    			Variables.LauncherAdapterPath,
    			Variables.NestedParameters)
    		.FirstOrDefault();
    	list.Add(new ReportingParameter(
					"Launcher Adapter Mass",
					typeof(double),
					launcherAdapterMass
		));

		// Get the fuel mass using its Path property from the ProductTree.
		var fuelMass = string.IsNullOrWhiteSpace(Variables.FuelMassPath) ? 0D : option.GetNestedParameterValuesByPath<double>(
    			Variables.FuelMassPath,
    			Variables.NestedParameters)
    		.FirstOrDefault();
    	list.Add(new ReportingParameter(
					"CPROP Fuel Mass",
					typeof(double),
					fuelMass
		));

		// Get the oxidizer mass using its Path property from the ProductTree.
		var oxidizerMass = string.IsNullOrWhiteSpace(Variables.OxidizerMassPath) ? 0D : option.GetNestedParameterValuesByPath<double>(
    			Variables.OxidizerMassPath,
    			Variables.NestedParameters)
    		.FirstOrDefault();
    	list.Add(new ReportingParameter(
					"CPROP Oxidizer Mass",
					typeof(double),
					oxidizerMass
		));

		// Get the pressurant mass using its Path property from the ProductTree.
		var pressurantMass = string.IsNullOrWhiteSpace(Variables.PressurantMassPath) ? 0D : option.GetNestedParameterValuesByPath<double>(
    			Variables.PressurantMassPath,
    			Variables.NestedParameters)
    		.First();
    	list.Add(new ReportingParameter(
					"CPROP Pressurant Mass",
					typeof(double),
					pressurantMass
		));

		// Get the propellant mass using its Path property from the ProductTree.
		var propellantMass = string.IsNullOrWhiteSpace(Variables.PropellantMassPath) ? 0D : option.GetNestedParameterValuesByPath<double>(
    			Variables.PropellantMassPath,
    			Variables.NestedParameters)
    		.FirstOrDefault();
		list.Add(new ReportingParameter(
					"EPROP Propellant Mass",
					typeof(double),
					propellantMass
		));


		var dataSet = dataSource as DataSet;
	    var dataTable = dataSet.Tables["MainData"];
		var subsystemNames = new List<string>(Variables.SubsystemNames);

		// Create a table of distinct owners that are currently available in the "calculated"
		// datasource and merge that with the list of default SubSystems / Owners
		foreach (DataRow row in new DataView(dataTable).ToTable(true, "OwnerShortName").Rows)
	    {
	    	var ownerShortName = row["OwnerShortName"].ToString();
	    	if (!subsystemNames.Contains(ownerShortName))
	    	{
	    		subsystemNames.Add(ownerShortName);
	    	}
	    }

	    // The report filter string to be set on the last added reporting filter
	    var reportFilterString = string.Empty;

	    // Create Product / Function parameters for every Owner
		foreach (var subSystem in subsystemNames.OrderBy(x => x))
		{
			var prodfuncParameter = new ReportingParameter(
					subSystem,
					typeof(string),
					"Product")
			.AddLookupValue("Product", "Product")
			.AddLookupValue("Function","Function")
			.AddLookupValue("Not used", "Not used");

			prodfuncParameter.ForceDefaultValue = false;

			if (reportFilterString != string.Empty)
			{
				reportFilterString += " OR ";
			}
			reportFilterString += "[ProductFunction] = ?" + ReportingParameter.NamePrefix + subSystem + " And [OwnerShortName] == '" + subSystem + "'";

			list.Add(prodfuncParameter);
		}

		// Create a DataView that contains all Distinct values in the resultDataSource's SystemName_1 column,
		var systemsTable = new DataView(dataTable).ToTable(true, "SystemName_1");
		ReportingParameter systemParameter = null;

		reportFilterString = "(" + reportFilterString + ") AND [SystemName_1] = ?dyn_System_Name";

		// Add a parameter with a lookup value for every "root" System found in the data.
		foreach (DataRow row in systemsTable.Rows)
		{
			var systemName = row["SystemName_1"].ToString();
			if (systemParameter == null)
			{
				systemParameter = new ReportingParameter(
					"System Name",
					typeof(string),
					systemName,
					reportFilterString
					);
					list.Insert(0, systemParameter);
			}
			systemParameter.AddLookupValue(systemName, systemName);
		}

		return list;
	}
}
