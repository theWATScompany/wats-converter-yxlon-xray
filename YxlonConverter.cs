using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Virinco.WATS.Integration.TextConverter;
using Virinco.WATS.Interface;
using Virinco.WATS.Interface.MES;
using Virinco.WATS.Interface.MES.Production;

namespace Virinco.WATS.Converter.Yxlon
{
    public class YxlonConverter : TextConverterBase
    {

        public YxlonConverter() : this(new Dictionary<string, string>()) { }

        public YxlonConverter(IDictionary<string, string> args) : base(args)
        {
            searchFields.AddExactField(UUTField.StartDateTime, ReportReadState.InHeader, "Created: ", "yyyy/MM/dd, HH:mm", typeof(DateTime));
            const string regExFileName = @"(?<PartNo>[^_^.]*)[_.](?<SN>\d{12})";
            SearchFields.RegExpSearchField s = searchFields.AddRegExpField(UUTField.UseSubFields, ReportReadState.InHeader, regExFileName, null, typeof(string));
            s.AddSubField("PartNo", typeof(string), null, UUTField.PartNumber);
            s.AddSubField("SN", typeof(string), null, UUTField.SerialNumber);
            const string regExResults = @"Results: (?<Pass>\d+)\s+(?<Failed>\d+)\s+(?<Info>\d+)";
            s = searchFields.AddRegExpField("Results", ReportReadState.InHeader, regExResults, null, typeof(string), ReportReadState.InTest);
            s.AddSubField("Pass", typeof(int));
            s.AddSubField("Failed", typeof(int));
            s.AddSubField("Info", typeof(int));
            searchFields.AddExactField("Comment", ReportReadState.InTest, "Comment:", null, typeof(string));
            s = searchFields.AddRegExpField("Mark", ReportReadState.InTest, @".+(?<StepName>Mark \d+)", null, typeof(string));
            s.AddSubField("StepName", typeof(string));
        }


        string currentStepName = "Mark";
        protected override bool ProcessMatchedLine(SearchFields.SearchMatch match, ref ReportReadState readState)
        {

            if (match == null)
            {
                MesInterface mes = new MesInterface();
                UnitInfo unit = mes.Production.GetUnitInfo(currentUUT.SerialNumber);
                if (unit == null)
                {
                    base.ParseError($"Serial number {currentUUT.SerialNumber} not found in WATS MES", apiRef.ConversionSource.SourceFile.FullName);
                    return false;
                }
                currentUUT.PartNumber = unit.PartNumber;
                currentUUT.PartRevisionNumber = unit.Revision;
                currentUUT.AddMiscUUTInfo("Line", base.converterArguments["productionLine"]);
                apiRef.Submit(SubmitMethod.Automatic, currentUUT);
                return true;
            }
            switch (match.matchField.fieldName)
            {
                case "Results":
                    if ((int)match.GetSubField("Failed") > 0)
                        currentUUT.Status = UUTStatusType.Failed;
                    break;
                case "Comment":
                    Regex rexComp = new Regex(@"([A-Z]{1,3}\d{1,3})");
                    Regex rexPercent = new Regex(@"(\d{1,3}V?%)");
                    Match mComp = rexComp.Match(match.completeLine);
                    Match mPercent = rexPercent.Match(match.completeLine);
                    int numTests = mComp.Groups.Count - 1;
                    if (mComp.Groups.Count != mPercent.Groups.Count)
                    {
                        //Error there should be pairs
                        //Decide how to handle, maybe use the one with the lowest count
                        if (mPercent.Groups.Count < numTests) numTests = mPercent.Groups.Count;
                    }
                    NumericLimitStep s = currentUUT.GetRootSequenceCall().AddNumericLimitStep(currentStepName);
                    for (int i = 0; i < numTests; i++)
                    {
                        double num = 0;
                        if (double.TryParse(mPercent.Groups[i].Value.Replace("%", "").Replace("V", ""), out num)) //V=Void just ignore for the moment
                        {
                            //Fill should be more than 75%
                            s.AddMultipleTest(num, CompOperatorType.GE, 75, "%", mComp.Groups[i].Value);
                        }
                        else
                        {
                            //No percent found
                        }
                    }
                    break;
                case "Mark":
                    currentStepName = (string)match.GetSubField("StepName");
                    break;
                default:
                    break;
            }

            //([a-z]{1,3}\d{1,3})
            //(\d{1,3}%)

            string line = match.completeLine;
            Console.WriteLine(line);
            return true;
        }
    }
}
