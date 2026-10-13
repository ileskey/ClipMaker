using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using ClipDownloader.Application.Models;
using ClipMaker.Media.Core.ValueObjects;

namespace ClipDownloader.Infrastructure.Downloads;

internal sealed record IndexedMediaReference(long Start,long Bytes,PresentationRange IndexRange,bool StartsWithSap,int SapType,uint SapDelta);
internal sealed class Mp4SidxIndex(byte[] initialization,uint referenceId,uint timescale,
    IEnumerable<IndexedMediaReference> references)
{
    private readonly byte[] initialization=initialization.ToArray();
    internal ReadOnlyMemory<byte> Initialization=>initialization;
    internal uint ReferenceId {get;}=referenceId;
    internal uint Timescale {get;}=timescale;
    internal IReadOnlyList<IndexedMediaReference> References {get;}=Array.AsReadOnly(references.ToArray());
}

// Input is a complete prefix from byte zero with independently confirmed full resource length.
internal sealed class Mp4SidxReader
{
    internal const int MaximumPrefixBytes=2*1024*1024;
    internal OperationResult<Mp4SidxIndex> Read(ReadOnlyMemory<byte> prefix,long resourceLength)
    {
        if(prefix.IsEmpty||prefix.Length>MaximumPrefixBytes||resourceLength<prefix.Length) return Fail(OperationErrorCode.InvalidMetadata);
        try
        {
            var init=new List<byte>();ReadOnlyMemory<byte>? index=null;long indexEnd=0;int position=0,count=0;
            bool ftyp=false,moov=false;
            while(position<prefix.Length)
            {
                if(++count>10_000||prefix.Length-position<8) throw new InvalidDataException();
                var tail=prefix.Span[position..];ulong size=BinaryPrimitives.ReadUInt32BigEndian(tail);int header=8;
                if(size==1){if(tail.Length<16)throw new InvalidDataException();size=BinaryPrimitives.ReadUInt64BigEndian(tail[8..]);header=16;}
                string type=Encoding.ASCII.GetString(tail.Slice(4,4));
                if(type is "moof" or "mdat") break; // No payload allocation; the prefix may end inside this box.
                if(size<(ulong)header||size>(ulong)tail.Length)throw new InvalidDataException();
                if(type is "ftyp" or "moov")
                {
                    if(type=="ftyp"){if(ftyp)throw new InvalidDataException();ftyp=true;}
                    else{if(moov)throw new InvalidDataException();moov=true;}
                    init.AddRange(prefix.Slice(position,(int)size).ToArray());
                }
                if(type=="sidx")
                {if(index is not null)throw new InvalidDataException();index=prefix.Slice(position+header,(int)size-header);indexEnd=position+(long)size;}
                position+=(int)size;
            }
            if(!ftyp||!moov||index is null)throw new InvalidDataException();
            var data=index.Value;byte version=data.Span[0];
            if(version>1)return Fail(OperationErrorCode.UnsupportedFormat);
            if(U32(data,0)!=((uint)version<<24))throw new InvalidDataException();
            uint id=U32(data,4),scale=U32(data,8);if(id==0||scale==0)throw new InvalidDataException();
            int at=12;BigInteger ticks;ulong firstOffset;
            if(version==0){ticks=U32(data,at);firstOffset=U32(data,at+4);at+=8;}
            else{if(data.Length<at+16)throw new InvalidDataException();ticks=BinaryPrimitives.ReadUInt64BigEndian(data.Span[at..]);firstOffset=BinaryPrimitives.ReadUInt64BigEndian(data.Span[(at+8)..]);at+=16;}
            if(data.Length<at+4)throw new InvalidDataException();
            ushort reserved=BinaryPrimitives.ReadUInt16BigEndian(data.Span[at..]);
            ushort entries=BinaryPrimitives.ReadUInt16BigEndian(data.Span[(at+2)..]);at+=4;
            if(reserved!=0||entries==0||data.Length!=at+entries*12)throw new InvalidDataException();
            long bytePosition=checked(indexEnd+checked((long)firstOffset));var references=new List<IndexedMediaReference>();
            for(int i=0;i<entries;i++)
            {
                uint size=U32(data,at),duration=U32(data,at+4),sap=U32(data,at+8);at+=12;
                if((size&0x80000000)!=0)return Fail(OperationErrorCode.UnsupportedFormat);
                if(size==0||duration==0)throw new InvalidDataException();
                long end=checked(bytePosition+size);if(bytePosition<indexEnd||end>resourceLength)throw new InvalidDataException();
                SignedMediaTime.TryCreate(ticks,scale,out var startTime,out _);ticks+=duration;
                SignedMediaTime.TryCreate(ticks,scale,out var endTime,out _);
                PresentationRange.TryCreate(startTime!,endTime!,out var range,out _);
                references.Add(new(bytePosition,size,range!, (sap&0x80000000)!=0,(int)((sap>>28)&7),sap&0x0fffffff));
                bytePosition=end;
            }
            return OperationResult<Mp4SidxIndex>.Succeeded(new(init.ToArray(),id,scale,references));
        }
        catch(Exception error)when(error is InvalidDataException or ArgumentOutOfRangeException or OverflowException or IndexOutOfRangeException)
        {return Fail(OperationErrorCode.InvalidMetadata);}
    }
    private static uint U32(ReadOnlyMemory<byte> data,int offset)
    {if(offset<0||data.Length-offset<4)throw new InvalidDataException();return BinaryPrimitives.ReadUInt32BigEndian(data.Span[offset..]);}
    private static OperationResult<Mp4SidxIndex> Fail(OperationErrorCode code)=>OperationResult<Mp4SidxIndex>.Failed(
        new(code,OperationErrorCategory.Execution,"원본 MP4 index/init을 제한된 직접 참조로 확인하지 못했습니다.","SourceIndex"));
}
