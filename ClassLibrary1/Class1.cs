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
        public struct Dist {
            public double length;
            public int pre;
        }
        public static readonly string[] ROOMFORBID = new string[1] { "kitchen" };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Get application and documnet objects
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uiapp.ActiveUIDocument.Document;
            Selection selection = uidoc.Selection;
            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            var EleIgnored = new List<ElementId>();
            //var ctg_id = new ElementId(BuiltInCategory.OST_Furniture);
            //EleIgnored.Add(ctg_id);

            var levelid = ViewLevel(doc);
            var rooms = GetRoomsOnLevel(doc, levelid);
            var RoomIds = rooms.ToList();
            string debug = "";
         

            //var rel = TravelDis(doc, EleIgnored, RoomForbid);
            //Report(rel, doc, RoomForbid);
            var RoomForbid = GetForbidRoom(doc, RoomIds);
            var rel = Graph(doc, RoomForbid);
            Report(rel, doc, RoomForbid);

            return Result.Succeeded;
        }
        private void Report(KeyValuePair<List<ElementId>, List<double>> result, Document doc, IList<ElementId> RoomForbid)
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
                if (dis > (MAX_NUM - 1)) finalReport += room.Name + "  " + "Error\n" + "There is no egress path from this room or obstacles on the startpoint!\n";
                else
                    finalReport += room.Name + "  " + dis.ToString() + "ft" + "\n";
            }
            TaskDialog.Show("Travel Distance", finalReport);
        }
        public KeyValuePair<List<ElementId>, List<double>> TravelDis(Document doc, ICollection<ElementId> selectedIds,List<ElementId> RoomsForbid) //distances of all rooms on current level to nearest exit
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

            using (TransactionGroup transGroup = new TransactionGroup(doc))
            {
                transGroup.Start("group start");
                using (Transaction trans_del = new Transaction(doc))
                {
                    trans_del.Start("Del");
                    foreach (ElementId id in RoomsForbid)
                    {
                        Element temp = doc.GetElement(id);
                        DeleteDoorsOfRoom(doc, id);
                    }
                    trans_del.Commit();
                }
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
                        foreach (XYZ r in rooms_loc)
                        {
                            double temp_len = 10000000;
                            XYZ temp_loc = null;
                            int cnt = 0;
                            foreach (XYZ d in doors_loc)
                            {
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

                var RoomsPoint = rooms_loc;

                using (Transaction trans2 = new Transaction(doc))
                {
                    if (trans2.Start("Path_final") == TransactionStatus.Started)
                    {

                        var settings = RouteAnalysisSettings.GetRouteAnalysisSettings(doc);

                        settings.SetIgnoredCategoryIds(selectedIds);
                        for (int i = 0; i < RoomsPoint.Count; i++)
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
                transGroup.Assimilate();
            }
            var allRoomName = new List<ElementId>();
            foreach (Room r in rooms) allRoomName.Add(r.Id);
            return new KeyValuePair<List<ElementId>, List<double>>(allRoomName, final_rel);

        }
        public IEnumerable<Room> GetRoomsOnLevel(Document doc, ElementId idLevel) //get all rooms on current level
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
            foreach (Curve c in p)
            {
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
            ElementClassFilter familyInstancefilter = new ElementClassFilter(typeof(FamilyInstance));
            ElementCategoryFilter doorfilter = new ElementCategoryFilter(BuiltInCategory.OST_Doors);
            LogicalAndFilter doorInstancefilter = new LogicalAndFilter(familyInstancefilter, doorfilter);
            FilteredElementCollector col = new FilteredElementCollector(doc);
            ICollection<ElementId> doors = col.WherePasses(doorInstancefilter).ToElementIds();
            var rel = new List<ElementId>();
            foreach (ElementId id in doors)
            {
                Element door = doc.GetElement(id);
                FamilyInstance doorfam = door as FamilyInstance;
                Parameter temp = doorfam.Symbol.LookupParameter("Function");
                if (temp.AsValueString() == "Exterior")
                {
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
        public void DeleteDoorsOfRoom(Document doc, ElementId dangerRoom)
        {
            ElementClassFilter familyInstancefilter = new ElementClassFilter(typeof(FamilyInstance));
            ElementCategoryFilter doorfilter = new ElementCategoryFilter(BuiltInCategory.OST_Doors);
            LogicalAndFilter doorInstancefilter = new LogicalAndFilter(familyInstancefilter, doorfilter);
            FilteredElementCollector col = new FilteredElementCollector(doc);
            ICollection<ElementId> doors = col.WherePasses(doorInstancefilter).ToElementIds();
            var rel = new List<ElementId>();
            string debug = "";
            
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
          
            //TaskDialog.Show("Revit", debug);

        }
        public IList<XYZ> CalPointOfRooms(Document doc, IEnumerable<Room> rooms, List<XYZ> Exit2Door, ICollection<ElementId> eleIg)
        {
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
                            PathOfTravel path = PathOfTravel.Create(doc.ActiveView, r, exit);
                            if (path == null) continue;
                            double dis = calDis(path.GetCurves());
                            if (dis < final_dis)
                            {
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
                        else
                        {
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
        public IList<XYZ> CenterOfRoom(Document doc, IEnumerable<Room> rooms)
        {
            var rel = new List<XYZ>();
            foreach (Room r in rooms)
            {
                LocationPoint loc = r.Location as LocationPoint;
                XYZ xyz = loc.Point;
                rel.Add(xyz);
            }
            return rel;
        }
        public double calHalfDia(Room room)
        {
            BoundingBoxXYZ box = room.get_BoundingBox(null);
            Transform trf = box.Transform;
            XYZ min_xyz = box.Min;
            XYZ minInCoor = trf.OfPoint(min_xyz);
            XYZ max_xyz = box.Max;
            XYZ maxInCoor = trf.OfPoint(max_xyz);
            XYZ point = new XYZ(maxInCoor.X, maxInCoor.Y, minInCoor.Z);
            return minInCoor.DistanceTo(point) / 2;
        }
        public KeyValuePair<List<ElementId>, List<double>> Graph(Document doc,IList<ElementId> RoomForbid)
        {
            var levelid = ViewLevel(doc);
            var rooms = GetRoomsOnLevel(doc, levelid);
            var doors = GetAllDoors(doc,levelid);
            var exits = GetExits(doc);
            var RoomIDs = new List<ElementId>();
            var DoorIDs = new List<ElementId>();
            var AllIDs = new List<ElementId>();
            foreach (ElementId d in doors)
            {
                DoorIDs.Add(d);
                AllIDs.Add(d);
            }
            foreach (Room r in rooms)
            {
                RoomIDs.Add(r.Id);
                AllIDs.Add(r.Id);
            }
            

            var mat_dim = RoomIDs.Count + DoorIDs.Count;
            int[,] mat = new int[100, 100];
            for (int i = 0; i < 100; i++)
                for (int j = 0; j < 100; j++)
                    mat[i, j] = 0;

            foreach (ElementId id in DoorIDs)
            {
                int count = 0;
                Element door = doc.GetElement(id);
                FamilyInstance doorfam = door as FamilyInstance;
                Room temp1 = doorfam.FromRoom;
                Room temp2 = doorfam.ToRoom;
                int offset = DoorIDs.Count;
                int dindex = DoorIDs.FindIndex(a => a.IntegerValue == id.IntegerValue);
                foreach (ElementId rid in RoomIDs)
                {
                    int rindex = RoomIDs.FindIndex(a => a.IntegerValue == rid.IntegerValue);
                    if (temp1 != null && temp1.Id == rid)
                    {
                        mat[dindex, offset + rindex] = 1;count++;
                        continue;
                    }
                    if (temp2 != null && temp2.Id == rid)
                    {
                        mat[dindex, offset + rindex] = 1;count++;
                        continue;
                    }
                }
              
            }

    
            var RoomLocs = new List<XYZ>();
            var DoorLocs = new List<XYZ>();
            var AllLocs = new List<XYZ>();
            var LocsForbid = new List<XYZ>();
            foreach (ElementId id in DoorIDs)
            {
                Element r = doc.GetElement(id);
                LocationPoint loc = r.Location as LocationPoint;
                XYZ xyz = loc.Point;
                DoorLocs.Add(xyz);
                AllLocs.Add(xyz);
            }
            foreach (ElementId id in RoomIDs)
            {
                Element r = doc.GetElement(id);
                LocationPoint loc = r.Location as LocationPoint;
                XYZ xyz = loc.Point;
                RoomLocs.Add(xyz);
                AllLocs.Add(xyz);
                if (RoomForbid.Contains(id))
                {
                    LocsForbid.Add(xyz);
                }
            }

            double[,] ajm = new double[100, 100];
            for (int i = 0; i < mat_dim; i++)
                for (int j = 0; j < mat_dim; j++)
                    ajm[i, j] = -1;
            IList<Curve>[,] pathMap = new IList<Curve>[mat_dim, mat_dim];
            using (Transaction trans = new Transaction(doc))
            {
                trans.Start("CAL");
                int offset = DoorIDs.Count;
                View view = doc.ActiveView;
                for (int i = 0; i < mat_dim; i++)
                    for (int j = 0; j < mat_dim; j++)
                    {
                        if (mat[i, j] == 0) continue;
                        if (LocsForbid.Contains(RoomLocs[j - offset])) continue;
                        PathOfTravel p = PathOfTravel.Create(view, DoorLocs[i], RoomLocs[j - offset]);
                        if (p == null) {
                           
                            continue;
                        }
                        var crs = p.GetCurves();
                        pathMap[i, j] = crs;
                        ajm[i, j] = calDis(crs);
                        ajm[j, i] = ajm[i, j];
                    }
                trans.Commit();
            }

            //for (int i = DoorIDs.Count; i < mat_dim; i++) {
            //    int tmp = 0;
            //    for (int j = 0; j < mat_dim; j++)
            //        if (ajm[i, j] > 0) tmp++;
            //    if(tmp==0) TaskDialog.Show("Revit", RoomIDs[i-DoorIDs.Count].ToString());
            //}

            foreach (ElementId fid in RoomForbid)
            {
                Element tmp = doc.GetElement(fid);
                if (!tmp.Category.Name.ToLower().Contains("room")) continue;
                int index = AllIDs.FindIndex(a => a.IntegerValue == fid.IntegerValue);
                for (int i = 0; i < AllIDs.Count; i++)
                {
                    ajm[index, i] = -1;
                    ajm[i, index] = -1;
                }
                TaskDialog.Show("revit", "delete a room");
            }
            for (int i = 0; i < AllIDs.Count; i++)
                for (int j = 0; j < AllIDs.Count; j++)
                {
                    if (i == j)
                    {
                        ajm[i, j] = 0;
                        continue;
                    }
                    if (ajm[i, j] < 0) ajm[i, j] = MAX_NUM;
                    
                }
            //string ttt = "";
            //for (int i = 0; i < AllIDs.Count; i++) {
            //    for (int j = 0; j < AllIDs.Count; j++)
            //        ttt += ajm[i, j].ToString() + "  ";

            //    ttt += "\n";
            //}
            //TaskDialog.Show("revit", ttt);
            var dis = GetFloyd(ajm,mat_dim);
            var final_rel = new List<double>();
            var final_des = new List<int>();
            foreach (ElementId rid in RoomIDs)
            {
                double len = MAX_NUM;
                int des_node = -1;
                int x = AllIDs.FindIndex(a => a.IntegerValue == rid.IntegerValue);
                foreach (ElementId did in exits)
                {
                    int y = AllIDs.FindIndex(a => a.IntegerValue == did.IntegerValue);
                    double tmp_len = dis[x, y].length;
                    if (len >= tmp_len)
                    {
                        len = tmp_len;
                        des_node = y;
                    }
                }
                final_rel.Add(len);
                final_des.Add(des_node);
            }

            var Final_path = new List<List<int>>();
            for(int i =0;i<RoomIDs.Count;i++)
            {
                var rid = RoomIDs[i];
                if (final_rel[i] > MAX_NUM - 1)
                {
                    Final_path.Add(null);
                    continue;
                }
                var nodes = new List<int>();
                var dst = final_des[i];
                int x = AllIDs.FindIndex(a => a.IntegerValue == rid.IntegerValue);
                nodes.Add(dst);
                int pre = dis[x, dst].pre;
                while (true)
                {
                    nodes.Add(pre);
                    if (pre == x)
                    {
                        break;
                    }
                    pre = dis[x, pre].pre;
                }
                nodes.Reverse();
                Final_path.Add(nodes);
            }

            return new KeyValuePair<List<ElementId>, List<double>>(RoomIDs, final_rel);
        }
        public IList<ElementId> GetAllDoors(Document doc,ElementId levelid)
        {
            ElementClassFilter familyInstancefilter = new ElementClassFilter(typeof(FamilyInstance));
            ElementCategoryFilter doorfilter = new ElementCategoryFilter(BuiltInCategory.OST_Doors);
            LogicalAndFilter doorInstancefilter = new LogicalAndFilter(familyInstancefilter, doorfilter);
            FilteredElementCollector col = new FilteredElementCollector(doc);
            ICollection<ElementId> doors = col.WherePasses(doorInstancefilter).ToElementIds();
            var rel = new List<ElementId>();
            foreach (ElementId id in doors)
            {
                Element door = doc.GetElement(id);
                if (door.LevelId == levelid) rel.Add(id);
            }
            return rel;
        }
        public  Dist[,] GetFloyd(double[,] G,int N) {
            int i, j, v;
            Dist[,] D = new Dist[N, N];
            for (i = 0; i < N; i++)
            {
                for (j = 0; j < N; j++)
                {
                    if (i == j)
                    {
                        D[i, j].length = 0;
                        D[i, j].pre = i;
                    }
                    else {
                        if (G[i, j] < MAX_NUM-1)
                        {
                            D[i, j].length = G[i, j];
                            D[i, j].pre = i;
                        }
                        else {
                            D[i, j].length = MAX_NUM;
                            D[i, j].pre = -1;
                        }
                    }
                }
            }

            for (v = 0; v < N; v++)
            {
                for (i = 0; i < N; i++)
                {
                    for (j = 0; j < N; j++)
                    {
                        if (D[i, j].length > (D[i, v].length + D[v, j].length))
                        {
                            D[i, j].length = D[i, v].length + D[v, j].length;
                            D[i, j].pre = D[v, j].pre;
                        }
                    }
                }
            }

            return D;
        }
        public IList<ElementId> GetForbidRoom(Document doc, IEnumerable<Room> Rooms)
        {
            var rel = new List<ElementId>(); 
            foreach (Room room in Rooms)
            {
                TaskDialog dialog = new TaskDialog("Is the Room Forbidden?");
                dialog.MainContent = room.Name + "\n" + "Is this room can not be passed?";
                dialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;

                TaskDialogResult result = dialog.Show();
                if (result == TaskDialogResult.Yes)
                {
                    rel.Add(room.Id);
                }
            }
            return rel;
        }
    }
}