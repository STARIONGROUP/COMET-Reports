// ----------------------------------------------------------------------------------------
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
// ----------------------------------------------------------------------------------------

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

    /// <summary>
    /// The ShortName of the System Mode need for Duty Cycle data
    /// </summary>
    public const string SystemModesShortName = "SM";
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

        var newView = new DataView(resultDataSource);
        newView.RowFilter = "Segments = '" + Variables.SpaceSegmentName + "' And (P_on <> 0 Or P_stby <> 0)";
        resultDataSource = newView.ToTable();
        resultDataSource.TableName = "MainData";



        //---------------------------------------------------
        // Create distinct rows for all ED's and summarize the NumberOfItems
        //---------------------------------------------------
        var resultColumnArray = resultDataSource.Columns.Cast<DataColumn>()
                                 .Select(x => x.ColumnName)
                                 .Except(new[] { "NumberOfItems", "n_items", "SubsystemName", "SystemName_3", "SystemName_4", "SystemName_5" })
                                 .ToArray();

        var distinctResultDataSource = new DataView(resultDataSource).ToTable(true, resultColumnArray);
        distinctResultDataSource.Columns.Add("NumberOfItems", typeof(double));

        // Summarize the relevant data and write to the merged DataRow
        foreach (DataRow distinctRow in distinctResultDataSource.Rows)
        {
            distinctRow["NumberOfItems"] = 0D;
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
                }
            }
        }
        resultDataSource = distinctResultDataSource;
        //---------------------------------------------------


        //Build dynamic tables
        var systemModeList = this.Iteration.ActualFiniteStateList.FirstOrDefault(x => x.ShortName == Variables.SystemModesShortName);

        if (systemModeList == null)
        {
            System.Windows.MessageBox.Show("Please set the correct ShortName of the System Mode ActualFiniteStateList in the Variables section of this report's code file.");
        }

        var stateList = systemModeList.ActualState.ToList().Select(x => x.ShortName);

		foreach (var state in stateList)
        {
        
            var newMeanColumn = new DataColumn("P_mean" + state, typeof(double));
            newMeanColumn.DefaultValue = 0D;
            resultDataSource.Columns.Add(newMeanColumn);

            var newMaxColumn = new DataColumn("P_max" + state, typeof(double));
            newMaxColumn.DefaultValue = 0D;
            resultDataSource.Columns.Add(newMaxColumn);

            this.DynamicTableCellsCollector.AddValueTableCell("dynamicDutyCycleHeaderTable", state);
            this.DynamicTableCellsCollector.AddValueTableCell("dynamicP_meanHeaderTable", state);
            this.DynamicTableCellsCollector.AddValueTableCell("dynamicP_maxHeaderTable", state);

            this.DynamicTableCellsCollector.AddFieldTableCell("dynamicDutyCycleTable", "P_duty_cyc" + state);
            this.DynamicTableCellsCollector.AddFieldTableCell("dynamicP_meanTable", "P_mean" + state);
            this.DynamicTableCellsCollector.AddFieldTableCell("dynamicP_maxTable", "P_max" + state);

            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_meanOwnerTotalsTable", "sumSum([P_mean" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_meanSystemTotalsTable", "sumSum([P_mean" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_meanTotalsTable", "sumSum([P_mean" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_meanTotalsInclMarginTable", "sumSum([P_mean" + state + "] * (1 + ?SystemMargin / 100))");

            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_maxOwnerTotalsTable", "sumSum([P_max" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_maxSystemTotalsTable", "sumSum([P_max" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_maxTotalsTable", "sumSum([P_max" + state + "])");
            this.DynamicTableCellsCollector.AddExpressionTableCell("dynamicP_maxTotalsInclMarginTable", "sumSum([P_max" + state + "] * (1 + (?SystemMargin / 100)))");

            foreach (DataRow row in resultDataSource.Rows)
            {
				var emptyValues = new [] {"", "-"}.ToList();

                var P_duty_cyc = emptyValues.Contains(row["P_duty_cyc" + state].ToString()) ? -1D : double.Parse(row["P_duty_cyc" + state].ToString());
                var redundancy_type = row["redundancy_type"].ToString();
                var redundancy_scheme = row["redundancy_scheme"].ToString();
                var redundancy_k = emptyValues.Contains(row["redundancy_k"].ToString()) ? 0D : double.Parse(row["redundancy_k"].ToString());
                var redundancy_n = emptyValues.Contains(row["redundancy_n"].ToString()) ? 0D : double.Parse(row["redundancy_n"].ToString());
                var P_on = emptyValues.Contains(row["P_on"].ToString()) ? 0D : double.Parse(row["P_on"].ToString());
                var P_stby = emptyValues.Contains(row["P_stby"].ToString()) ? 0D : double.Parse(row["P_stby"].ToString());
                var NumberOfItems = emptyValues.Contains(row["NumberOfItems"].ToString()) ? 0D : double.Parse(row["NumberOfItems"].ToString());
                var maturity_margin = emptyValues.Contains(row["maturity_margin"].ToString()) ? 0D : double.Parse(row["maturity_margin"].ToString());
                
                // --------------------------------------------------------
                // Calculate P_mean
                // --------------------------------------------------------
                var P_mean = 0D;

                if (P_duty_cyc != -1)
                {
                	P_mean = double.NaN;

					if (redundancy_type == "External")
                    {
                        if (redundancy_scheme == "Cold")
                        {
                            P_mean = (redundancy_k / redundancy_n) * NumberOfItems * ((P_on * P_duty_cyc) + (P_stby * (1 - P_duty_cyc))) * (1 + maturity_margin / 100);
                        }
                        else if (redundancy_scheme == "Standby")
                        {
                        	P_mean = ((redundancy_k / redundancy_n) * NumberOfItems * ((P_on * P_duty_cyc) + (P_stby * (1 - P_duty_cyc)))) + (((redundancy_n - redundancy_k) / redundancy_n) *  NumberOfItems  * P_stby) * (1 + maturity_margin / 100);
                        }
                    }
                    
                    if (Double.IsNaN(P_mean))
                    {
                        P_mean = NumberOfItems * ((P_on * P_duty_cyc) + (P_stby * (1 - P_duty_cyc))) * (1 + maturity_margin / 100);
                    }
                }

                row["P_mean" + state] = P_mean;


                // --------------------------------------------------------
                // Calculate P_max
                // --------------------------------------------------------
                var P_max = 0D;

                if (P_duty_cyc != -1)
                {
                	P_max = double.NaN;
                    if (P_duty_cyc == 0)
                    {
                    
                        if (redundancy_type == "External")
                        {
                            if (redundancy_scheme == "Cold")
                            {
                                P_max= (redundancy_k / redundancy_n) * NumberOfItems * P_stby * (1 + maturity_margin / 100);
                            }
                        }
                        
                        if (Double.IsNaN(P_max))
                        {
                        	P_max = NumberOfItems * P_stby * (1 + maturity_margin / 100);
                        }
                    }
                    else
                    {
                        if (redundancy_type == "External")
                        {
                            if (redundancy_scheme == "Cold")
                            {
                                P_max= (redundancy_k / redundancy_n) * NumberOfItems  * P_on * (1 + maturity_margin / 100);
                            }
                            else if (redundancy_scheme == "Standby")
                            {
                                P_max = ((redundancy_k / redundancy_n) * NumberOfItems  * P_on) + (((redundancy_n - redundancy_k) / redundancy_n) *  NumberOfItems * P_stby) * (1 + maturity_margin / 100);
                            }
                        }
                        
                        if (Double.IsNaN(P_max))
                        {
                            P_max = NumberOfItems * P_on * (1 + maturity_margin / 100);
                        }
                    }
                }

                row["P_max" + state] = P_max;
            }
        }
        
        return resultDataSource;
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

    [DefinedThingShortName("redundancy", "redundancy")]
    public DataCollectorCompoundParameter<RowRepresentation> redundancy { get; set; }

    [DefinedThingShortName("P_on", "P_on")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterOn { get; set; }

    [DefinedThingShortName("P_stby", "P_stby")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterStby { get; set; }

    [DefinedThingShortName("P_peak", "P_peak")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterPeak { get; set; }

    [DefinedThingShortName("P_duty_cyc", "P_duty_cyc")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterDutyCyc { get; set; }

    [DefinedThingShortName("n_items")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterNumberOfItems { get; set; }

    [DefinedThingShortName("maturity_margin")]
    public DataCollectorDoubleParameter<RowRepresentation> parameterMaturityMargin { get; set; }

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

    // Gets the ElementDefinition's name
    public string ElementDefinitionName
    {
        get { return this.GetElementDefinition().Name.Trim() + "(s)"; }
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
    /// Checks if this is Function or Product related data.
    /// This column is used for checking the Reporting Parameters (Product/Function/Not used)
    /// </summary>
    public string ProductFunction
    {
        get { return functionsCategory.Value ? "Function" : (productsCategory.Value ? "Product" : ""); }
    }

    // Gets the ElementDefinition for a specific ElementBase.
    private ElementDefinition GetElementDefinition()
    {
        ElementDefinition elementDefinition;
        var elementUsage = this.ElementBase as ElementUsage;

        if (elementUsage == null)
        {
            elementDefinition = this.ElementBase as ElementDefinition;
        }
        else
        {
            elementDefinition = elementUsage.ElementDefinition;
        }

        return elementDefinition;
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
    public override IEnumerable<IReportingParameter> CreateParameters(object dataSource, IDataCollector dataCollector)
    {
        var list = new List<IReportingParameter>();
        var dataTable = dataSource as DataTable;

        // Create the MissionName ReportingParameter 
        var missionName = dataTable.Rows.Count > 0 ? dataTable.Rows[0]["Missions"] : "Unknown";
        var missionNameParameter = new ReportingParameter(
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
                "[ProductFunction] = ?" + ReportingParameter.NamePrefix + "ProductFunction")
        .AddLookupValue("Product", "Product")
        .AddLookupValue("Function", "Function")
        );

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

