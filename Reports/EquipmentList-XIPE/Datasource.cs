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
		// comply to the Hierarchy of categories defined here.
		// The Category hierarchy of elements in the product tree should be like:
		//
		//   Missions
		//   | Segments
		//     | Elements [1..5 nesting levels]
		//       | Equipment
		//
		// In case there are multiple nested Equipment levels in the model, the deepest level is selected
		// as the source for the parameter values.
		var productHierarchy = new CategoryDecompositionHierarchy
	        .Builder(this.Iteration)
	        .AddLevel("Missions")
			.AddLevel("Segments")
			.AddLevel("Elements", 5)
	        .AddLevel("Equipment")
	        .Build();

		// Build a DataTable for the productHierarchy level (Product level)
	    var resultDataSource =
	        new DataCollectorNodesCreator<MainDataRow>()
	        	.GetTable(productHierarchy, nestedElementTree);



		//---------------------------------------------------
		// Create distinct rows for all elements and summarize the NoOfItems
		//---------------------------------------------------
		var resultColumnArray = resultDataSource.Columns.Cast<DataColumn>()
                                 .Select(x => x.ColumnName)
                                 .Except(new [] {"NumberOfItems", "n_items", "Equipment"})
                                 .ToArray();  

		var distinctResultDataSource = new DataView(resultDataSource).ToTable(true, resultColumnArray);
		distinctResultDataSource.Columns.Add("NumberOfItems", typeof(double));
		distinctResultDataSource.Columns.Add("Equipment", typeof(string));

		foreach (DataRow distinctRow in distinctResultDataSource.Rows)
		{
			distinctRow["NumberOfItems"] = 0D;
			var writeElementDefinitionName = false;
			foreach (DataRow row in resultDataSource.Rows) 
			{
				var write = true;
				foreach (var columnName in resultColumnArray) 
				{
					if (!row[columnName].Equals(distinctRow[columnName]))
					{
						write = false;
						break;
					}
				}
				if (write) 
				{
					distinctRow["NumberOfItems"] = ((double)distinctRow["NumberOfItems"]) + ((double)row["NumberOfItems"]);

					if (writeElementDefinitionName) 
					{
						distinctRow["Equipment"] = (string)row["ElementDefinitionName"];
					}
					else 
					{
						distinctRow["Equipment"] = (string)row["Equipment"];
					}

					writeElementDefinitionName = true;
				}
			}
		}
		resultDataSource = distinctResultDataSource;
		//---------------------------------------------------



		// Create a DataView that contains all Distinct values in the resultDataSource's Segments column,
		var segmentsTable = new DataView(resultDataSource).ToTable(true, "Segments");

		// Create the DataSet that will be returned by this CreateDataObject method.
		var dataSet = new DataSet();

		// Find the data rows that contain a Segments name that is equal to the SpaceSegmentName set in the top of this file
		// and add that table to the DataSet.
		foreach (DataRow dataRow in segmentsTable.Rows)
		{
			var segment = dataRow["Segments"].ToString();

			if ((segment) == Variables.SpaceSegmentName)
			{
				var newView = new DataView(resultDataSource);
				newView.RowFilter = "Segments = '" + segment + "'";
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
public class MainDataRow : DataCollectorRow
{
	/// <summary>
	/// The Parameter classes.
	/// Need to be public.
	/// </summary>
	[DefinedThingShortName("m", "MassWithoutMargin")]
	public DataCollectorDoubleParameter<MainDataRow> parameterMass { get; set; }

	[DefinedThingShortName("mass_margin", "MassMargin")]
	public DataCollectorDoubleParameter<MainDataRow> parameterMassMargin { get; set; }

	[DefinedThingShortName("n_items")]
	public DataCollectorDoubleParameter<MainDataRow> parameterNumberOfItems { get; set; }

	/// <summary>
	/// The implementation of the Mass property/column in the result datasource
	/// Includes the calculated margin.
	/// </summary>
	public double Mass
	{
	    get { return this.parameterMass.Value * (100 + this.parameterMassMargin.Value) / 100; }
	}

	/// <summary>
	/// The implementation of the TotalMass property/column in the result datasource
	/// </summary>
	public double TotalMass
	{
	    get { return this.Mass * this.NumberOfItems; }
	}

	/// <summary>
	/// The implementation of the NumberOfItems property/column in the result datasource
	/// </summary>
	public double NumberOfItems
	{
	    get { return this.parameterNumberOfItems.Value == 0D ? 1D : this.parameterNumberOfItems.Value; }
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
	/// The implementation of the ElementDefinitionName property/column in the result datasource.
	/// this.ElementBase is a default property of the abstract DataCollectorRow class,
	/// of which this class is derived from. 
	/// this.ElementBase can be an ElementDefinition, or ElementUsage.
	/// </summary>
	public string ElementDefinitionName
	{
	    get 
	    {
			if (this.ElementBase is ElementDefinition) 
	    	{
	    		return this.ElementBase.Name;
	    	}
	    	
	    	if (this.ElementBase is ElementUsage) 
	    	{
	    		return ((ElementUsage)this.ElementBase).ElementDefinition.Name;
	    	}

			return null;
    	}
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
