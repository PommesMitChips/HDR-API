using System;
using System.Collections.Generic;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        static double[] ExportDisplayVolume(Scene scene)
        {
            var result=new double[scene.TableVolume==null?2:8];
            result[0]=scene.VolumeRange;result[1]=scene.TableVolumeEnabled?1:0;
            if(scene.TableVolume!=null)Array.Copy(scene.TableVolume,0,result,2,6);
            return result;
        }
        static HoloLcdData ExportLcdLayout(Scene scene)
        {
            return new HoloLcdData{Columns=scene.LcdColumns,Rows=scene.LcdRows,Column=scene.LcdColumn,Row=scene.LcdRow,
                Width=scene.LcdWidth,Height=scene.LcdHeight,SourceId=scene.LcdSourceId,CallerId=scene.LcdCallerId,Renderer=scene.LcdRenderer,RefreshHz=scene.LcdRefreshHz};
        }
        static void ImportDisplayConfiguration(Scene scene,HoloSceneData data)
        {
            if(data.Volume!=null)
            {
                var values=data.Volume;if(values.Length!=2&&values.Length!=8)throw new ArgumentException("Invalid display volume configuration.");
                foreach(double value in values)if(!Geometry.Finite(value)||Math.Abs(value)>25)throw new ArgumentException("Invalid display volume bounds.");
                if(values[0]<0||(values[1]!=0&&values[1]!=1))throw new ArgumentException("Invalid display range/volume flag.");
                scene.VolumeRange=values[0];scene.TableVolumeEnabled=values[1]==1;
                if(values.Length==8)
                {
                    if(values[3]<=0||values[4]<=0||values[5]<=0||values[6]<values[4]||values[7]<values[5])throw new ArgumentException("Invalid table volume dimensions.");
                    scene.TableVolume=new double[6];Array.Copy(values,2,scene.TableVolume,0,6);
                }
            }
            if(data.Lcd!=null)
            {
                var lcd=data.Lcd;LcdProjection.Validate(lcd.Width,lcd.Height,lcd.Columns,lcd.Rows,lcd.Column,lcd.Row);
                if(lcd.Renderer<0||lcd.Renderer>1)throw new ArgumentException("Invalid LCD renderer.");LcdRefreshSchedule.Validate(lcd.RefreshHz);
                if(lcd.SourceId!=0&&lcd.CallerId==0)throw new ArgumentException("LCD groups require an owner.");
                scene.LcdColumns=lcd.Columns;scene.LcdRows=lcd.Rows;scene.LcdColumn=lcd.Column;scene.LcdRow=lcd.Row;
                scene.LcdWidth=lcd.Width;scene.LcdHeight=lcd.Height;scene.LcdSourceId=lcd.SourceId;scene.LcdCallerId=lcd.CallerId;
                scene.LcdRenderer=lcd.Renderer;scene.LcdRefreshHz=lcd.RefreshHz;
            }
        }
        static void ValidateLcdGroups(Dictionary<long,Scene> scenes)
        {
            var tiles=new HashSet<string>();
            foreach(var tile in scenes.Values)
            {
                if(tile.LcdSourceId==0)continue;
                Scene source;if(!scenes.TryGetValue(tile.LcdSourceId,out source))continue; // Removed anchor: blank remaining tiles.
                if(source.LcdSourceId!=0&&source.LcdSourceId!=source.ConsoleId||source.LcdCallerId!=tile.LcdCallerId
                    ||source.LcdColumns!=tile.LcdColumns||source.LcdRows!=tile.LcdRows||source.LcdWidth!=tile.LcdWidth||source.LcdHeight!=tile.LcdHeight||source.LcdRenderer!=tile.LcdRenderer||source.LcdRefreshHz!=tile.LcdRefreshHz)
                    throw new ArgumentException("Invalid LCD group reference.");
                string key=tile.LcdSourceId+":"+tile.LcdColumn+":"+tile.LcdRow;
                if(!tiles.Add(key))throw new ArgumentException("Duplicate LCD group tile.");
            }
        }
    }
}
