﻿using CommandLine;
using Dapper;
using Npgsql;
using System;
using System.Diagnostics;
using System.Reflection;
using tesselate_building_core;
using Wkx;

namespace tesselate_building_sample_console
{
    class Program
    {
        static string password = string.Empty;
        static void Main(string[] args)
        {
            var version = Assembly.GetEntryAssembly().GetName().Version;
            Console.WriteLine($"Tool: Tesselate buildings {version}");
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
            {
                o.User = string.IsNullOrEmpty(o.User) ? Environment.UserName : o.User;
                o.Database = string.IsNullOrEmpty(o.Database) ? Environment.UserName : o.Database;

                var outputProjection = (o.Format == "mapbox" ? 3857 : 4978);

                var connectionString = $"Host={o.Host};Username={o.User};Database={o.Database};Port={o.Port}";

                var istrusted = TrustedConnectionChecker.HasTrustedConnection(connectionString);

                if (!istrusted)
                {
                    Console.Write($"Password for user {o.User}: ");
                    password = PasswordAsker.GetPassword();
                    connectionString += $";password={password}";
                    Console.WriteLine();
                }
                var conn = new NpgsqlConnection(connectionString);
                SqlMapper.AddTypeHandler(new GeometryTypeHandler());
                conn.Open();


                var select = $"select ST_AsBinary({o.InputGeometryColumn}) as geometry, {o.HeightColumn} as height, style, {o.IdColumn} as id";
                var sql = $"{select} from {o.Table}";

                var buildings = conn.Query<Building>(sql);

                var i = 1;
                foreach (var building in buildings)
                {
                    var polygon = (Polygon)building.Geometry;
                    var wktFootprint = polygon.SerializeString<WktSerializer>();
                    var height = building.Height;
                    var points = polygon.ExteriorRing.Points;

                    var buildingZ = 0; //put everything on the ground
                    var res = TesselateBuilding.MakeBuilding(polygon, buildingZ, height, building.BuildingStyle);
                    var wkt = res.polyhedral.SerializeString<WktSerializer>();

                    var colors = "{"+ string.Join(',', res.colors) +"}";
                    var updateSql = $"update {o.Table} set {o.OutputGeometryColumn} = ST_Transform(ST_Force3D(St_SetSrid(ST_GeomFromText('{wkt}'), 4326)), {outputProjection}) " +
                    $", {o.ColorsColumn} = '{colors}' where {o.IdColumn}={building.Id}";
                    conn.Execute(updateSql);
                    var perc = Math.Round((double)i / buildings.AsList().Count * 100, 2);
                    Console.Write($"\rProgress: {perc.ToString("F")}%");
                    i++;
                }

                conn.Close();

                stopWatch.Stop();
                Console.WriteLine();
                Console.WriteLine($"Elapsed: {stopWatch.ElapsedMilliseconds / 1000} seconds");
                Console.WriteLine("Program finished.");
            });
        }
    }
}
