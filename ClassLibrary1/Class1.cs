﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Analysis;
namespace Lab1PlaceGroup
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Class1 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Get application and documnet objects
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uiapp.ActiveUIDocument.Document;
            Selection selection = uidoc.Selection;
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            var EleIgnored = new List<ElementId>();
            foreach (ElementId id in selectedIds)
            {
                Element temp = doc.GetElement(id);
                if (temp.Category.Id.ToString() ==  BuiltInCategory.OST_Rooms.ToString())
                {
                    DeleteDoorsOfRoom(doc, id);
                    TaskDialog.Show("revit", "Room!");
                }
                else
                    EleIgnored.Add(id);
            }
            //TravelDis(doc,EleIgnored);
            return Result.Succeeded;
        }
        public void TravelDis(Document doc,ICollection<ElementId> selectedIds) //distances of all rooms on current level to nearest exit
        {

            View currentView = doc.ActiveView;

            //door location
            var doors = new List<ElementId>();
            doors = GetExits(doc);
            var doors_loc = new List<XYZ>();
            foreach (ElementId id in doors)
            {
                Element door = doc.GetElement(id);
                LocationPoint loc = door.Location as LocationPoint;
                XYZ xyz = loc.Point;
                doors_loc.Add(xyz);
            }
            //room location
            var levelid = ViewLevel(doc);
            var rooms = GetRoomsOnLevel(doc, levelid);
            var rooms_loc = new List<XYZ>();
            foreach (Room r in rooms)
            {
                LocationPoint loc = r.Location as LocationPoint;
                XYZ xyz = loc.Point;
                rooms_loc.Add(xyz);
            }

            //TaskDialog.Show("Revit", doors_loc.Count.ToString());
            //TaskDialog.Show("Revit", rooms_loc.Count.ToString());
            var Exit2Door = new List<XYZ>();
            using (Transaction trans = new Transaction(doc))
            {
                if (trans.Start("Path") == TransactionStatus.Started)
                {


                    //PathOfTravel.CreateMapped(currentView, rooms_loc, doors_loc);

                    //try to find the shortest path to the exits(one of)
                    var ig = new List<ElementId>();
                    var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);
                    foreach (ElementId id in selectedIds)
                    {
                        Element temp = doc.GetElement(id);
                        ig.Add(temp.Category.Id);
                    }
                    settings.SetIgnoredCategoryIds(ig);
                    foreach (XYZ r in rooms_loc) {
                        double temp_len = 10000000;
                        XYZ temp_loc = null;
                        int cnt = 0;
                        foreach (XYZ d in doors_loc) {
                            PathOfTravel path = PathOfTravel.Create(currentView, r, d);
                            if (path == null) continue;
                            IList<Curve> p = path.GetCurves();
                            if (temp_len >= calDis(p))
                            {
                                temp_loc = d;
                                temp_len = calDis(p);
                            }
                            
                        }
                        Exit2Door.Add(temp_loc);
                    }                 
                    trans.RollBack();
                    
                    //TaskDialog taskdialog = new TaskDialog("Revit");
                    //taskdialog.MainContent = "Click [OK] to commot and click [cancel] to roll back";
                    //TaskDialogCommonButtons buttons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                    //taskdialog.CommonButtons = buttons;
                    //if (TaskDialogResult.Ok == taskdialog.Show())
                    //{
                    //    if (TransactionStatus.Committed != trans.Commit()) {
                    //        TaskDialog.Show("Fail", "Trans can not be committed");
                    //    }
                    //}
                    //else {
                    //    trans.RollBack();
                    //}
                }
            }
            using (Transaction trans2 = new Transaction(doc))
            {
                if (trans2.Start("Path_final") == TransactionStatus.Started)
                {
                    var ig = new List<ElementId>();
                    var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);
                    foreach (ElementId id in selectedIds)
                    {
                        Element temp = doc.GetElement(id);
                        ig.Add(temp.Category.Id);
                    }
                    settings.SetIgnoredCategoryIds(ig);
                    for (int i =0;i<rooms_loc.Count;i++)
                    {
                        XYZ d = Exit2Door[i];
                        XYZ r = rooms_loc[i];
                        if (r == null || d == null)
                        {
                            TaskDialog.Show("null", "null");
                            continue;
                        };
                        PathOfTravel.Create(currentView, r, d);
                    }
                    trans2.Commit();
                }
            }
        }
        public IEnumerable<Room> GetRoomsOnLevel(Document doc,ElementId idLevel) //get all rooms on current level
        {
            return new FilteredElementCollector(doc)
              .WhereElementIsNotElementType()
              .OfClass(typeof(SpatialElement))
              .Where(e => e.GetType() == typeof(Room))
              .Where(e => e.LevelId.IntegerValue.Equals(
               idLevel.IntegerValue))
              .Cast<Room>();
        }
        public Double calDis(IList<Curve> p) //cal the lenght of a travel path
        {
            double rel = 0;
            foreach (Curve c in p) {
                rel += c.Length;
            }
            return rel;
        }
        public ElementId ViewLevel(Document doc)//get view of the current level
        {
            View active = doc.ActiveView;
            ElementId levelId = null;

            Parameter level = active.LookupParameter("Associated Level");

            FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
            ICollection<Element> lvlCollection = lvlCollector.OfClass(typeof(Level)).ToElements();

            foreach (Element l in lvlCollection)
            {
                Level lvl = l as Level;
                if (lvl.Name == level.AsString())
                {
                    levelId = lvl.Id;
                    //TaskDialog.Show("test", lvl.Name + "\n"  + lvl.Id.ToString());
                }
            }
            return levelId;

        }
        public List<ElementId> GetExits(Document doc) //get all elements which are exits
        {
            ElementClassFilter familyInstancefilter  = new ElementClassFilter(typeof(FamilyInstance));
            ElementCategoryFilter doorfilter = new ElementCategoryFilter(BuiltInCategory.OST_Doors);
            LogicalAndFilter doorInstancefilter = new LogicalAndFilter(familyInstancefilter, doorfilter);
            FilteredElementCollector col = new FilteredElementCollector(doc);
            ICollection<ElementId> doors = col.WherePasses(doorInstancefilter).ToElementIds();
            var rel = new List<ElementId>();
            foreach (ElementId id in doors) {
                Element door = doc.GetElement(id);
                FamilyInstance doorfam = door as FamilyInstance;
                Parameter temp = doorfam.Symbol.LookupParameter("Function");
                if (temp.AsValueString() == "Exterior") {
                    rel.Add(id);
                }
            }
            //TaskDialog.Show("Revit", rel.Count.ToString());
            return rel;
        }
        public void DeleteEle(Document document, Element element)
        {
            // Delete an element via its id
            ElementId elementId = element.Id;
            ICollection<ElementId> deletedIdSet = document.Delete(elementId);

            if (0 == deletedIdSet.Count)
            {
                throw new Exception("Deleting the selected element in Revit failed.");
            }

            String prompt = "The selected element has been removed and ";
            prompt += deletedIdSet.Count - 1;
            prompt += " more dependent elements have also been removed.";

            // Give the user some information
            //TaskDialog.Show("Revit", prompt);
        }
        public void DeleteDoorsOfRoom(Document doc,ElementId dangerRoom)
        {
            ElementClassFilter familyInstancefilter = new ElementClassFilter(typeof(FamilyInstance));
            ElementCategoryFilter doorfilter = new ElementCategoryFilter(BuiltInCategory.OST_Doors);
            LogicalAndFilter doorInstancefilter = new LogicalAndFilter(familyInstancefilter, doorfilter);
            FilteredElementCollector col = new FilteredElementCollector(doc);
            ICollection<ElementId> doors = col.WherePasses(doorInstancefilter).ToElementIds();
            var rel = new List<ElementId>();
            string debug = "";
            using (Transaction trans = new Transaction(doc))
            {
                if (trans.Start("Path") == TransactionStatus.Started)
                {
                    foreach (ElementId id in doors)
                    {
                        Element door = doc.GetElement(id);
                        FamilyInstance doorfam = door as FamilyInstance;
                        Room temp1 = doorfam.FromRoom;
                        Room temp2 = doorfam.ToRoom;
                        if (temp1 != null && temp1.Id == dangerRoom)
                        {
                            DeleteEle(doc, door);
                            continue;
                        }
                        if (temp2 != null && temp2.Id == dangerRoom)
                        {
                            DeleteEle(doc, door);
                            continue;
                        }
                    }
                    trans.Commit();
                }
            }
            //TaskDialog.Show("Revit", debug);
      
        }
    }
}