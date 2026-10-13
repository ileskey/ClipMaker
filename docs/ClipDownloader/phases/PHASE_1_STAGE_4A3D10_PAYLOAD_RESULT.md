# Phase 1 4a-3d-10 bounded elementary payload correspondence

2026-10-14, 시작 HEAD6568b26, phase/1, Base 유지.

H.264 AVCC/Annex B VCL 대응 reader, carrier packet 관찰 parser, local same-file payload/time bridge를 구현했다. 각 reader는 대응 관찰을 반환하며 decoder/config equality나 SharedAxis fact를 만들지 않는다. 최종 producer는 decoded picture의 별도 gate와 provider 선언·실제 source 초기 provenance를 함께 확인해야 한다.

H264PicturePayloadReader는4MiB/4096NAL cap, NAL length/startcode/forbidden bit bounds를 검사한다. non-VCL framing을 제외하고 trailing zero를 정규화한 length+VCL을 hash하며 IDR 포함/slice 수를 보존한다. 이것은 decoded equality의 대체 근거가 아니다.

carrier parser는 strictUTF8/JSON16MiB/4096packets/총payload4MiB, single H264 video clock1/90000, duplicate/stream/flags/data size/hex offset을 검사한다. 누락 PTS는 null로 보존하며 추정하지 않는다.

PresentationPayloadBridge는 원본 indexed file을 read-share lock/pre/post hash로 재확인하고 native sample byte 범위를 읽는다. video는 순서/VCL identity 및 explicit PTS와 원본 movie edit의 exact 관계를 대조한다. audio는 원본 AAC payload와 ADTS payload,1024sample 진행/rate/channels,ID3 carrier의 floor rescale를 대조한다. movie edit 없는 audio를 첫 source sample부터 대조하는 제한 전략이다. decoder/PCM/history를 보증하지 않는다.

자동667개 통과(신규23), 실패/skip0. VCL framing/invalid bounds,carrier 누락PTS/ambiguous data,timestamp/payload/file change/cancel 거절을 확인했다. 첫 bridge tests4건에서 InvalidDataException이 결과로 변환되지 않고 누출돼 catch를 수정하고 재실행했다. 솔루션 build 경고0/오류0.

실제로 보존한137/140과 새270/234 초기 파일을 새 helper로 대조했다. source는 actual native gate를 통과했다. 새 다운로드는 없다. carrier video의 local FFprobe JSON은5839464bytes/180pictures/누락PTS1/IDR picture1이었다.

|대조|payload 수|explicit timestamp anchors|누락PTS|결과|
|---|---:|---:|---:|---|
|video|180|179|1|Succeeded|
|audio|259|1 (ID3)|0|Succeeded|

실험 source의 index range placeholder는 byte/native 대조 전용이며 초기 source/index provenance 증명에 쓰지 않는다. 실제 FFprobe carrier 실행은 shell harness이며 새 production ProcessRunner wrapper 통합 확인이 아니다. decoded pixel gate·producer/public registry 연결·target/전체4a/4단계는 미완료다.
