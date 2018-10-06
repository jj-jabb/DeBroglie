﻿using DeBroglie.Models;
using DeBroglie.Topo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiledLib;
using TiledLib.Layer;

namespace DeBroglie.Console
{
    public class TiledMapLoader : ISampleSetLoader
    {
        internal static void AddTileset(IDictionary<string, Tile> tilesByName, ITileset tileset)
        {
            if (tileset.TileProperties == null)
                return;
            foreach (var kv in tileset.TileProperties)
            {
                var localTileId = kv.Key;
                var properties = kv.Value;
                if (properties.TryGetValue("name", out var name))
                {
                    tilesByName[name] = new Tile(tileset.FirstGid + localTileId);
                }
            }
        }

        public SampleSet Load(string filename)
        {
            var srcFilename = filename;
            var map = TiledUtil.Load(filename);
            // Scan tilesets for tiles with a custom property "name"
            var tilesByName = new Dictionary<string, Tile>();
            foreach (var tileset in map.Tilesets)
            {
                AddTileset(tilesByName, tileset);
            }
            ITopoArray<Tile> sample;
            if (map.Orientation == Orientation.hexagonal)
            {
                // Read a single layer
                var layer = (TileLayer)map.Layers[0];
                sample = TiledUtil.ReadLayer(map, layer);
            }
            else
            {
                // Read all the layers into a 3d array.
                var tileLayers = map.Layers
                    .Where(x => x is TileLayer)
                    .Cast<TileLayer>()
                    .ToList();
                Tile[,,] results = null;
                Topology topology = null;
                for (var z = 0; z < tileLayers.Count; z++)
                {
                    var layer = tileLayers[z];
                    var layerArray = TiledUtil.ReadLayer(map, layer);
                    if (z == 0)
                    {
                        topology = layerArray.Topology;
                        results = new Tile[topology.Width, topology.Height, tileLayers.Count];
                    }
                    for (var y = 0; y < topology.Height; y++)
                    {
                        for (var x = 0; x < topology.Width; x++)
                        {
                            results[x, y, z] = layerArray.Get(x, y);
                        }
                    }
                }
                if (tileLayers.Count > 1 && topology.Directions.Type == DirectionsType.Cartesian2d)
                {
                    topology = new Topology(Directions.Cartesian3d, map.Width, map.Height, tileLayers.Count, false, false, false);
                }
                else
                {
                    topology = new Topology(topology.Directions, topology.Width, topology.Height, tileLayers.Count, false, false, false);
                }
                sample = TopoArray.Create(results, topology);
            }

            return new SampleSet
            {
                Directions = sample.Topology.Directions,
                Samples = new[] { sample },
                TilesByName = tilesByName,
                Template = new object[] { map, srcFilename },
            };
        }

        public Tile Parse(string s)
        {
            int tileId;
            if (int.TryParse(s, out tileId))
            {
                return new Tile(tileId);
            }
            throw new Exception($"Found no tile named {s}, either set the \"name\" property or use tile gids.");
        }
    }

    public class TiledMapSaver : ISampleSetSaver
    {
        public void Save(TileModel model, TilePropagator tilePropagator, string filename, DeBroglieConfig config, object template)
        {
            var templateArray = template as object[];
            var map = (Map)templateArray[0];
            var srcFilename = (string)templateArray[1];

            var layerArray = tilePropagator.ToArray();
            map.Layers = new BaseLayer[layerArray.Topology.Depth];
            for(var z = 0; z < layerArray.Topology.Depth; z++)
            {
                map.Layers[z] = TiledUtil.MakeTileLayer(map, layerArray, z);
            }
            map.Width = map.Layers[0].Width;
            map.Height = map.Layers[0].Height;
            TiledUtil.Save(filename, map);

            // Check for any external files that may also need copying
            foreach(var tileset in map.Tilesets)
            {
                if(tileset is ExternalTileset e)
                {
                    var srcPath = Path.Combine(Path.GetDirectoryName(srcFilename), e.source);
                    var destPath = Path.Combine(Path.GetDirectoryName(filename), e.source);
                    if (File.Exists(srcPath) && !File.Exists(destPath))
                    {
                        File.Copy(srcPath, destPath);
                    }
                }
                if(tileset.ImagePath != null)
                {
                    var srcImagePath = Path.Combine(Path.GetDirectoryName(srcFilename), tileset.ImagePath);
                    var destImagePath = Path.Combine(Path.GetDirectoryName(filename), tileset.ImagePath);
                    if(File.Exists(srcImagePath) && !File.Exists(destImagePath))
                    {
                        File.Copy(srcImagePath, destImagePath);
                    }
                }
            }
        }
    }
}
