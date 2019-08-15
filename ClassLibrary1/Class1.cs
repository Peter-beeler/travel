using System;
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
        const int ROOMID = -2000160;
        const int MAX_NUM = 999999999;
        public static readonly string[] ROOMFORBID = new string[1] { "kitchen"};
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Get application and documnet objects
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uiapp.ActiveUIDocument.Document;
            Selection selection = uidoc.Selection;
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            var EleIgnored = new List<ElementId>();
            var RoomForbid = new List<ElementId>();
            //var ctg_id = new ElementId(BuiltInCategory.OST_Furniture);
            //EleIgnored.Add(ctg_id);

            var levelid = ViewLevel(doc);
            var rooms = GetRoomsOnLevel(doc, levelid);
            string debug = "";
            foreach(Room room in rooms) {
                int flag = 0;
                debug += room.Name;
                foreach (string roomForbid in ROOMFORBID) {
                    if (room.Name.ToLower().Contains(roomForbid)) flag = 1;
                }
                if (flag == 1) RoomForbid.Add(room.Id);
            }

            foreach (ElementId id in RoomForbid)
            {
                Element temp = doc.GetElement(id);
                DeleteDoorsOfRoom(doc, id);
            }
            var rel = TravelDis(doc, EleIgnored);
            Report(rel, doc, RoomForbid);
            return Result.Succeeded;
        }
        private void Report(KeyValuePair<List<ElementId>, List<double>> result,Document doc,List<ElementId> RoomForbid)
        {
            var allRooms = result.Key;
            var Distance = result.Value;
            ElementId room_id;
            double dis;
            string finalReport = "";
            for (int i = 0; i < allRooms.Count; i++)
            {
                room_id = allRooms[i];
                if (RoomForbid.Contains(room_id)) continue;
                Element room = doc.GetElement(room_id);
                dis = Distance[i];
                if(dis>(MAX_NUM-1)) finalReport += room.Name + "  " + "Error\n" + "There is no egress path from this room or obstacles on the startpoint!\n";
                else
                    finalReport += room.Name + "  " + dis.ToString() + "ft"+"\n";
            }
            TaskDialog.Show("Travel Distance", finalReport);
        }
        public KeyValuePair<List<ElementId>,List<double>> TravelDis(Document doc,ICollection<ElementId> selectedIds) //distances of all rooms on current level to nearest exit
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
            var final_rel = new List<double>();
            var rooms_loc = CenterOfRoom(doc, rooms);

            //TaskDialog.Show("Revit", doors_loc.Count.ToString());
            //TaskDialog.Show("Revit", rooms_loc.Count.ToString());
            var Exit2Door = new List<XYZ>();
            using (Transaction trans = new Transaction(doc))
            {
                if (trans.Start("Path") == TransactionStatus.Started)
                {


                    //PathOfTravel.CreateMapped(currentView, rooms_loc, doors_loc);

                    //try to find the shortest path to the exits(one of)
                    //var ig = new List<ElementId>();
                    var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);
                    //foreach (ElementId id in selectedIds)
                    //{
                    //    Element temp = doc.GetElement(id);
                    //    ig.Add(temp.Category.Id);
                    //}
                    settings.SetIgnoredCategoryIds(selectedIds);
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

            var RoomsPoint = CalPointOfRooms(doc, rooms, Exit2Door,selectedIds);

            using (Transaction trans2 = new Transaction(doc))
            {
                if (trans2.Start("Path_final") == TransactionStatus.Started)
                {
                    
                    var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);
                    
                    settings.SetIgnoredCategoryIds(selectedIds);
                    for (int i =0;i<RoomsPoint.Count;i++)
                    {
                        XYZ d = Exit2Door[i];
                        XYZ r = RoomsPoint[i];
                        Room temp_room = doc.GetRoomAtPoint(r);
                        double halfDia = calHalfDia(temp_room);
                        if (r == null || d == null)
                        {
                            final_rel.Add(MAX_NUM);
                            continue;
                        };
                        IList<Curve> path = PathOfTravel.Create(currentView, r, d).GetCurves();
                        final_rel.Add(calDis(path));
                    }
                    trans2.Commit();
                }
            }

            var allRoomName = new List<ElementId>();
            foreach (Room r in rooms) allRoomName.Add(r.Id);
            return new KeyValuePair<List<ElementId>, List<double>>(allRoomName, final_rel);

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
                if (trans.Start("Del") == TransactionStatus.Started)
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
        public IList<XYZ> CalPointOfRooms(Document doc, IEnumerable<Room> rooms, List<XYZ> Exit2Door,ICollection<ElementId> eleIg) {
            var rel = new List<XYZ>();
            using (Transaction trans = new Transaction(doc))
            {
                if (trans.Start("Path") == TransactionStatus.Started)
                {
                    int count = 0;
                    var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);
                    settings.SetIgnoredCategoryIds(eleIg);
                    foreach (Room room in rooms)
                    {
                        var exit = Exit2Door[count];
                        BoundingBoxXYZ box = room.get_BoundingBox(null);
                        Transform trf = box.Transform;
                        XYZ min_xyz = box.Min;
                        XYZ max_xyz = box.Max;
                        XYZ minInCoor = trf.OfPoint(min_xyz);
                        XYZ maxInCoor = trf.OfPoint(max_xyz);
                        List<XYZ> temp = new List<XYZ>();
                        temp.Add(new XYZ(minInCoor.X, maxInCoor.Y, minInCoor.Z));
                        temp.Add(new XYZ(minInCoor.Y, maxInCoor.X, minInCoor.Z));
                        temp.Add(new XYZ(maxInCoor.X, minInCoor.Y, minInCoor.Z));
                        temp.Add(new XYZ(maxInCoor.Y, minInCoor.X, minInCoor.Z));

                        XYZ final = null;
                        double final_dis = MAX_NUM;
                        foreach (XYZ r in temp)
                        {
                            if (!room.IsPointInRoom(r)) continue;
                            PathOfTravel path =  PathOfTravel.Create(doc.ActiveView, r, exit);
                            if (path == null) continue;
                            double dis = calDis(path.GetCurves());
                            if (dis < final_dis) {
                                final_dis = dis;
                                final = r;
                            }
                        }
                        if (final == null)
                        {
                            LocationPoint loc = room.Location as LocationPoint;
                            XYZ xyz = loc.Point;
                            rel.Add(xyz);
                        }
                        else {
                            rel.Add(final);
                        }
                    }
                    trans.RollBack();
                }
            }
            
            //foreach (Room r in rooms)
            //{
            //    LocationPoint loc = r.Location as LocationPoint;
            //    XYZ xyz = loc.Point;
            //    rel.Add(xyz);
            //}
            return rel;
        }
        public IList<XYZ> CenterOfRoom(Document doc, IEnumerable<Room> rooms) {
            var rel = new List<XYZ>();
            foreach (Room r in rooms)
            {
                LocationPoint loc = r.Location as LocationPoint;
                XYZ xyz = loc.Point;
                rel.Add(xyz);
            }
            return rel;
        }
        public double calHalfDia(Room room) {
            BoundingBoxXYZ box = room.get_BoundingBox(null);
            Transform trf = box.Transform;
            XYZ min_xyz = box.Min;
            XYZ minInCoor = trf.OfPoint(min_xyz);
            XYZ max_xyz = box.Max;
            XYZ maxInCoor = trf.OfPoint(max_xyz);
            XYZ point = new XYZ(maxInCoor.X, maxInCoor.Y, minInCoor.Z);
            return minInCoor.DistanceTo(point)/2;
        }
        public void CurveColor(PathOfTravel p)
        {
            
        }
    }
}