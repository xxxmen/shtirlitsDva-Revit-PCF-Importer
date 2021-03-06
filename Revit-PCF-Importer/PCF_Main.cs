#region Namespaces
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using iv = PCF_Functions.InputVars;
using mySettings = Revit_PCF_Importer.Properties.Settings;
using BuildingCoder;
using PCF_Functions;

#endregion

namespace Revit_PCF_Importer
{
    public class PCFImport
    {
        //Declare the element collector
        public static ElementCollection ExtractedElementCollection;
        public static Autodesk.Revit.DB.Document doc; //This code to expose doc to class, because I don't want to pass it to each method in the chain;
        //See http://forums.autodesk.com/t5/revit-api/accessing-the-document-from-c-form-externalcommanddata-issue/td-p/4773407;
        //Declare static dictionary for parsing
        public static PCF_Dictionary PcfDict;
        //Declare static dictionary for creating
        public static PCF_Creator PcfCreator;

        public Result ExecuteMyCommand(UIApplication uiApp, ref string message)
        {
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Application app = uiApp.Application;
            doc = uidoc.Document;
            PcfDict = new PCF_Dictionary(new KeywordProcessor());

            ExtractedElementCollection = new ElementCollection();

            //Read the input PCF file
            FileReader fileReader = new FileReader();
            string[] readFile = fileReader.ReadFile(mySettings.Default.pcfPath);

            //This method collects all top-level element strings and creates ElementSymbols with data
            Parser.CreateInitialElementList(ExtractedElementCollection, readFile);

            //This method compares all element symbols and gets the number of lines for their definition
            Parser.IndexElementDefinitions(ExtractedElementCollection, readFile);

            //This method extracts element data from the file
            Parser.ExtractElementDefinition(ExtractedElementCollection, readFile);

            //This method processes elements
            foreach (ElementSymbol elementSymbol in ExtractedElementCollection.Elements)
            {
                PcfDict.ProcessTopLevelKeywords(elementSymbol);
            }

            #region CONFIGURATION

            DataSet dataSet = PCF_Configuration.ImportExcelToDataSet(mySettings.Default.excelPath);

            foreach (ElementSymbol es in
                from ElementSymbol es in ExtractedElementCollection.Elements
                where !( //Filter out non pipeline elements
                    string.Equals(es.PipelineReference, "PRE-PIPELINE") ||
                    string.Equals(es.PipelineReference, "MATERIALS") ||
                    string.Equals(es.ElementType, "PIPELINE-REFERENCE")
                    )
                select es)
                
            {
                PCF_Configuration.ExtractElementConfiguration(dataSet, es);
            }

            #endregion

            #region Element creation

            using (TransactionGroup txGp = new TransactionGroup(doc))
            {
                txGp.Start("Create elements from PCF data");

                using (Transaction trans1 = new Transaction(doc))
                {
                    trans1.Start("Create elements");
                    PcfCreator = new PCF_Creator(new ProcessElements());
                    //This method creates elements
                    //First send pipes for creation, other elements after
                    //Filter for pipes
                    var pipeQuery = from ElementSymbol es in ExtractedElementCollection.Elements
                        where string.Equals(es.ElementType, "PIPE")
                        select es;
                    //Send pipes to creation
                    foreach (ElementSymbol es in pipeQuery) PcfCreator.SendElementsToCreation(es);
                    //Regenerate document
                    doc.Regenerate();
                    //The rest of the elements are sent in waves, because fx. I determined, that CAPs must be sent later
                    //It depends on if the element can be created as standalone or it would need other elements to be present
                    //CAPS must be sent later
                    var firstWaveElementsQuery = from ElementSymbol es in ExtractedElementCollection.Elements
                        where
                            !( //Take care! ! operator has lower precedence than ||
                                string.Equals(es.ElementType, "PIPE") ||
                                string.Equals(es.ElementType, "CAP") ||
                                string.Equals(es.ElementType, "FLANGE-BLIND") ||
                                string.Equals(es.ElementType, "OLET") ||
                                string.Equals(es.ElementType, "VALVE")
                                )
                        select es;
                    //Send elements to creation
                    foreach (ElementSymbol es in firstWaveElementsQuery) PcfCreator.SendElementsToCreation(es);
                    trans1.Commit();
                }

                using (Transaction trans2 = new Transaction(doc))
                {
                    trans2.Start("Create second wave of elements");
                    //Filter CAPs 
                    var secondWaveElementsQuery = from ElementSymbol es in ExtractedElementCollection.Elements
                        where 
                        string.Equals(es.ElementType, "CAP") ||
                        string.Equals(es.ElementType, "FLANGE-BLIND") ||
                        string.Equals(es.ElementType, "OLET")
                        select es;
                    //Send CAPs to creation
                    foreach (ElementSymbol es in secondWaveElementsQuery) PcfCreator.SendElementsToCreation(es);
                    trans2.Commit();
                }

                using (Transaction trans3 = new Transaction(doc))
                {
                    trans3.Start("Create third wave of elements");
                    //Filter CAPs 
                    var thirdWaveElementsQuery = from ElementSymbol es in ExtractedElementCollection.Elements
                                                  where string.Equals(es.ElementType, "VALVE")
                                                  select es;
                    //Send CAPs to creation
                    foreach (ElementSymbol es in thirdWaveElementsQuery) PcfCreator.SendElementsToCreation(es);
                    trans3.Commit();
                }

                using (Transaction tx = new Transaction(doc))
                {
                    tx.Start("Delete dummy elements");
                    IEnumerable<Element> query = from ElementSymbol es in ExtractedElementCollection.Elements
                        where es.DummyToDelete != null
                        select es.DummyToDelete;
                    try
                    {
                        foreach (Element e in query)
                        {
                            doc.Delete(e.Id);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    tx.Commit();
                }
                txGp.Assimilate();
            }

            #endregion
        


        //Test
            //int test = ExtractedElementCollection.Elements.Count;

            //using (Transaction tx = new Transaction(doc))
            //{
            //    tx.Start("Transaction Name");
            //    tx.Commit();
            //}

            return Result.Succeeded;
        }
    }
}
