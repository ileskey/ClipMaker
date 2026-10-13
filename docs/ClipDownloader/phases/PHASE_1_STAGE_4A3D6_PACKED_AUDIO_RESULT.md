# Phase 1 4a-3d-6 packed AAC reference observations

2026-10-14. 시작 HEAD83f12a9, phase/1, Base 유지.

단일 complete packed-audio segment의 ID3 PRIV timestamp와 ADTS payload 관찰 reader를 구현했다. ID3v2.3/v2.4의 flags0·synchsafe/tag/frame bounds·unique timestamp를 검사한다. 33-bit carrier 범위 밖을 masking하여 정상화하지 않는다. 태그64KiB/frames64, segment4MiB/ADTS10000개 상한을 둔다.

MPEG-4 AAC-LC mono/stereo 단일 raw-data-block의 payload SHA256·rate/channels·1024 samples를 별도로 보존한다. source movie clock이나 audio demuxer의 상대 PTS를 발행하지 않는다. frame CRC 자체나 decode 준비를 검증했다고 주장하지 않는다. 미지원 version/flags/profile/blocks/config 및 malformed/누락/중복/불완전 byte는 Data=null이다.

Downloader608개 통과(새13개), 실패/skip0. 보존된 `.artifacts/phase1-g4b2b-shared-axis-20261012-01/234-initial.ts`를 새 reader로 읽어98614bytes·carrier0·259frames·44100Hz/stereo를 확인했다. 새 다운로드는 없다. helper의 성공은 provider grouping/content/native 연결이나 public SharedAxis Verified와 다르다. bridge producer·target·전체4a/4단계는 미완료다.
