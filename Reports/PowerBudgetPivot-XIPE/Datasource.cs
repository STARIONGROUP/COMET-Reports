// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DataSource.cs" company="Starion Group S.A.">
//    Copyright (c) 2015-2025 Starion Group S.A.
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

		// Contains all available parameter names and the order in which their column data should be shown in the report
        public static Dictionary<string, int> ParameterOrder =
            new Dictionary<string, int>
            {
                { "P_on", 1 },
                { "P_stby", 2 },
                { "P_peak", 3 },
                { "P_duty_cyc", 4 },
                { "P_mean", 5 }
            };
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

		// Get the tree of NestedElements for the selected Option.
		var nestedElementTree = new NestedElementTreeGenerator().Generate(option).ToList();

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
		// The third parameter in the GetTable method indicates that elements that do not contain
		// any wanted parameters will not be shown in the result table.
		var resultDataSource =
	        new DataCollectorNodesCreator<RowRepresentation>()
	        	.GetTable(productHierarchy, nestedElementTree, true);

		// Build a DataTable for the functionHierarchy level (Function level)
		// The third parameter in the GetTable method indicates that elements that do not contain
		// any wanted parameters will not be shown in the result table.
	    var secondDataSource =
	        new DataCollectorNodesCreator<RowRepresentation>()
	        	.GetTable(functionHierarchy, nestedElementTree, true);

		// Merge the two datatables
		resultDataSource.Merge(secondDataSource);

		// Find the ParameterName in the Variables.ParameterOrder and set the Order property for that datarow using the Variables.ParameterOrder property.
		foreach (DataRow dataRow in resultDataSource.Rows)
		{
			var parameterName = dataRow["ParameterName"].ToString();
			if (Variables.ParameterOrder.ContainsKey(parameterName))
			{
				dataRow["Order"] = Variables.ParameterOrder[parameterName];
			}
			else
			{
				System.Windows.MessageBox.Show("Unknown Parameter Name '" + parameterName + "' not found. Please add it to the Variables.ParameterOrder property in the Code Editor.");

				return null;
			}
		}

		// Create a DataView that contains all Distinct values in the resultDataSource's Segments column,
		var segmentsTable = new DataView(resultDataSource).ToTable(true, "Segments");

		// Find the data rows that contain a Segments name that is equal to the SpaceSegmentName set in the top of this file
		// and add that table to the DataSet.
		foreach (DataRow dataRow in segmentsTable.Rows)
		{
			var segment = dataRow["Segments"].ToString();

			if ((segment) == Variables.SpaceSegmentName)
			{
				var newView = new DataView(resultDataSource);
				newView.RowFilter = "Segments = '" + segment + "'";
				newView.Sort = "ParameterName ASC, OwnerShortName ASC, SubsystemName ASC";
				var newTable = newView.ToTable();
				newTable.TableName = "MainData";

				// Return the table
				return newTable;
			}
		}

		// If no table that contains the desired Variables.SpaceSegmentName was found then return null.
		return null;
	}
}

/// <summary>
/// Class that defines the row representation.
/// Every property that has a public getter will be used in the result datasource
/// </summary>
public class RowRepresentation : DataCollectorRow
{
	/// <summary>
	/// The Parameter classes for which we want to collect data.
	/// Need to be public.
	/// </summary>

	[DefinedThingShortName("P_on", "P_on")]
	public DataCollectorStateDependentPerRowDoubleParameter<RowRepresentation> parameterOn { get; set; }

	[DefinedThingShortName("P_stby", "P_stby")]
	public DataCollectorStateDependentPerRowDoubleParameter<RowRepresentation> parameterStby { get; set; }

	[DefinedThingShortName("P_peak", "P_peak")]
	public DataCollectorStateDependentPerRowDoubleParameter<RowRepresentation> parameterPeak { get; set; }

	[DefinedThingShortName("P_duty_cyc", "P_duty_cyc")]
	public DataCollectorStateDependentPerRowDoubleParameter<RowRepresentation> parameterDutyCyc { get; set; }

	[DefinedThingShortName("P_mean", "P_mean")]
	public DataCollectorStateDependentPerRowDoubleParameter<RowRepresentation> parameterMean { get; set; }

	/// <summary>
	/// The Category classes.
	/// Need to be public.
	/// </summary>
	[DefinedThingShortName("Functions")]
	public DataCollectorCategory<RowRepresentation> functionsCategory { get; set; }

	[DefinedThingShortName("Products")]
	public DataCollectorCategory<RowRepresentation> productsCategory { get; set; }

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

	// Gets the order in which the ParameterName columns need to be ordered in the result pivot.
	// Is set after DataTable creation
	public int Order { get { return 0; } }
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
		var dataTable = dataSource as DataTable;

		// Create the MissionName ReportingParameter
		var missionName = dataTable.Rows.Count > 0 ?  dataTable.Rows[0]["Missions"] : "Unknown";
		var missionNameParameter = new 	ReportingParameter(
						"MissionName",
						typeof(string),
						missionName);
		missionNameParameter.Visible = false;

		list.Add(missionNameParameter);

		// Create the Product / Function selection Reporting parameter
		list.Add(
			new ReportingParameter(
				"ProductFunction",
				typeof(string),
				"Product",
				"[ProductFunction] = ?" + ReportingParameter.NamePrefix + "ProductFunction" )
		.AddLookupValue("Product", "Product")
		.AddLookupValue("Function","Function")
		);

		// Create the Parameter selection ReportingParameter
		var parameterTypeSelection = new ReportingParameter(
				"ParameterName",
				typeof(string),
				"P_mean");

		// Add a LookupValue for every ParameterName found in Variables.ParameterOrder
		foreach (var keyValuePair in Variables.ParameterOrder)
		{
			parameterTypeSelection.AddLookupValue(keyValuePair.Key, keyValuePair.Key);
		}
		parameterTypeSelection.IsMultiValue = true;

		list.Add(parameterTypeSelection);


	    var optionDependentDataCollector = dataCollector as IOptionDependentDataCollector;

		// Get the selected option.
		var optionName = optionDependentDataCollector.SelectedOption.Name;

		// Create a dynamic parameter for use in the report header
		var optionNameParameter = new ReportingParameter(
					"OptionName",
					typeof(string),
					optionName);
		optionNameParameter.Visible = false;
		list.Add(optionNameParameter);

		return list;
	}
}
