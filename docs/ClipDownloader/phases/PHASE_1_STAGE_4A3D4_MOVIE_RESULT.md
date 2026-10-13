# Phase 1 4a-3d-4 limited movie/native interpretation

2026-10-14. 시작 HEAD b040597, phase/1. Base 유지.

실제 새137/140 첫 참조에서 확인한 LC zero-padding 및 단일 content edit의 해석을 구현했다. LC GAS 2-byte 설정 뒤 최대16bytes까지 zero byte만 허용하고 전체 extradata 크기/hash를 유지한다. nonzero extension·큰 config는 UnsupportedFormat이다. 이번140은 원본 `1210`+zero bytes로 총16bytes였으며 과거5-byte sync extension 사례를 같은 것으로 가정하지 않는다.

IndexedCollectedReference의 단일 track에 한해 empty가 아닌 positive-duration/rate1의 단일 edit를 허용한다. 원본 sample PTS/DTS에 명시 media_time을 한 번만 빼서 actual demux packet/frame과 exact 비교한다. full movie duration을 source 끝으로 발행하지 않는다. sample clipping/empty/multiple/rate edit·skip/discard는 현재 지원 범위에서 거절한다. HLS는 기존 no-edit 조건을 유지한다.

Downloader595개 통과/0 실패/skip. 새9개는 LC2/16bytes identity·nonzero extension/17byte 거절, 정확한 edit·이중 적용·empty/rate/clipping 거절을 검증한다. 실제 보존 파일은 다운로드 없이 local probe/native gate에 다시 입력했다.

|검사|137|140|
|---|---|---|
|codec config bytes|45|16|
|native sample count|180|430|
|movie timescale|15360|44100|
|edit|duration3470848/media_time512/rate1|없음|
|local probe/native gate|Succeeded/Succeeded|Succeeded/Succeeded|

실험 harness의 index range placeholder는 local byte/native gate 검사 용도다. source 최초 provenance/index↔movie 관계 또는 A/V 공통 축의 근거로 쓰지 않았다. 이전 actual indexed collector의 소유 파일을 그대로 검사했고 raw JSON/서명 URI는 공개하지 않았다. 양 파일의 시작값만으로 공통 축을 확정하지 않는다. public indexed/분리 관계·target·전체4a/4단계는 여전히 진행 중이다.
