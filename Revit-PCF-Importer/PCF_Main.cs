#region Namespaces
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using BuildingCoder;
using PCF_Functions;

#endregion

namespace Revit_PCF_Importer
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PCFImport : IExternalCommand
    {
        //Declare the element collector
        public static ElementCollection ExtractedElementCollection;
        public static Autodesk.Revit.DB.Document doc; //This code to expose doc to class, because I don't want to pass it to each method in the chain;
        //See http://forums.autodesk.com/t5/revit-api/accessing-the-document-from-c-form-externalcommanddata-issue/td-p/4773407;
        //Declare static dictionary for parsing
        public static PCF_Dictionary PcfDict;
        //Declare static dictionary for creating
        public static PCF_Creator PcfCreator;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            doc = uidoc.Document;
            PcfDict = new PCF_Dictionary(new KeywordProcessor());

            ExtractedElementCollection = new ElementCollection();

            //Read the input file
            FileReader fileReader = new FileReader();
            string[] readFile = fileReader.ReadFile();
            ;
            //This method collects all top-level element strings and creates ElementSymbols with data
            Parser.CreateInitialElementList(ExtractedElementCollection, readFile);
            ;
            //This method compares all element symbols and gets the amount of line for their definition
            Parser.IndexElementDefinitions(ExtractedElementCollection, readFile);
            ;
            //This method extracts element data from the file
            Parser.ExtractElementDefinition(ExtractedElementCollection, readFile);
            ;
            
            //This method processes elements
            foreach (ElementSymbol elementSymbol in ExtractedElementCollection.Elements)
            {
                PcfDict.ProcessTopLevelKeywords(elementSymbol);
            }
            ;
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Create elements");
                PcfCreator = new PCF_Creator(new ProcessElements());
                //This method creates elements
                foreach (ElementSymbol es in ExtractedElementCollection.Elements)
                {
                    PcfCreator.SendElementsToCreation(es);
                }
                tx.Commit();
            }
            ;

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
