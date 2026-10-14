# Phase 1 후속 — 움직이는 H.264 영상의 recovery-point 관찰 대응

2026-10-15. 시작 HEAD `5f7e71c`, branch `phase/1`에서 사용자 `qnjEE-wkw8k` / best137+140 / 30fps / Accurate /10~40초의 `NativeSyncState` 실패를 진단했다. 사용자 이전 정지 영상의 테스트 통과 보고를 이 움직이는 영상 검증으로 확대하지 않는다. 시작의 미추적 `22.mp4`, `out.mp4`, `out2.mp4`와 사용자 `out3.mp4`는 조작하지 않았다.

## 실제 원인

초기 video136 samples/packets/frames와 audio430개는 기존 native gate를 통과했다. 요청 범위를 확보한 video1272개 참조에서 ordinal1106/1134/1236이 실패했다. 세 native flags는0x00010000(non-sync), FFprobe packet flags는K__이며 위치/size/PTS/DTS/duration은 정확히 일치했다. video 단일 edit MediaTime512의 translated PTS를 사용한다.

보존 파일 `.artifacts/motion-target-sync/1c71cd3961464f69984f8604bfdd3909/indexed-ref-1098feb3be8e4cffa711d3c6b36f4c13.mp4`는1164835 bytes·SHA256 `1aadcedff13b04a3e20b21d4f01d4be02298327d9aa047d9eb264273bb0d3535`다. 불일치 packet의AVC4-byte framing을 실제 바이트로 읽었다. 모두 NAL6의 `06 06 01 C4 80` recovery-point SEI와 NAL1의 non-IDR intra slice를 포함한다. recovery count0/exact_match1/broken_link0/changing_group0이다.

[FFmpeg8.1 parser](https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavcodec/h264_parser.c#L339)는 recovery-point SEI가 있으면 key_frame을 설정한다. 따라서 **native container sync 표식과 codec/demux K 표식은 항상 같은 의미가 아니다.** K만으로 폐쇄형 IDR 시작을 증명하지 않는다. 기존 strict equality는 이 실제 대응을 잘못 거절했다.

## 제한된 지원 변경

Media의 `H264RecoveryPointReader`는 최대4MiB packet·4096NAL·4-byte AVC length와 bounded RBSP slice header를 읽는다. 확인한 정확한 SEI 형태 하나와 모든 VCL의non-IDR intra slice만 지원한다. 다른 SEI/count/flags, IDR/inter slice, 중복 recovery, 부족한 header/framing·다른 NAL 조합은 proof를 발행하지 않는다.

동일 read-lock 파일에서 native non-sync/FFprobe K packet의 track/position/size에 연결해 packetSHA와 지원하는 SEI/slice 사실을 만든다. 전체 file identity를 실행 전후와 증거 읽기 후에 확인한다. raw packet K와 native flags는 변경하지 않는다. 공유 probe와 획득용 initial probe에서 같은 reader를 사용하며 내부/shared 관찰 round-trip도 증거를 보존한다.

native/output gate는 video의 non-sync+K 차이를 이 바이트 증거가 정확히 연결된 경우에만 허용한다. missing/wrong proof·위치/크기/hash 형태·audio·다른 key 모순은 계속 실패한다. 기존 byte/clock/sample/frame/codec·decode·payload/picture/출력 검사와64MiB/60초 예산은 유지한다. **Fast의 `AllSlicesIdr` 조건은 변경하지 않았고 recovery point를 폐쇄형 시작점으로 승격하지 않는다.** 이번 새 실제 생성 증거는 Accurate다.

## 중간 실패와 최종 실제 결과

- 최초 수정 후 실제CLI는 별도 `SourceIndex`에서 실패했다. root cause는 미확정이다. 현재 별도64KiB prefix/SIDX 재조회는 양 stream에서 성공했다. 이를 원래 sync 오류나 native 코드 수정으로 해결한 네트워크 문제라고 표현하지 않는다. 제품 자동 retry를 추가하지 않았다.
- 공유 probe의 보존 파일 재검사는 성공했으나 다음CLI는 `NativeSyncState`를 다시 반환했다. 획득 전용 initial probe가 공유 probe와 별개인 경로에서 byte 증거를 만들지 않은 누락을 확인해 연결했다. 중간 성공을 전체CLI 성공으로 세지 않는다.
- 최종CLI는 같은 입력/best/Accurate/`--yes`로 exit0/final 확정에 성공했다. job `21abf385-4ecc-444b-aa2d-314a04e0ab69`, final `.artifacts/motion-accurate-cli/9156cb9a79d94e73bd25dc6c3db2279e/accurate.mp4`,2636718 bytes·SHA256 `54cc74d562fbb5c34c97afb326427ab2857abf27f03780cef71e9e3e8a108261`다.

요청·실제 video/audio는 모두 `[10,40)`초, output AAC skip1024/tail8·zero삽입/중복제거0, SourceEndVerified=false다. 최종 MP4의 독립 전체 A/V decode exit0과 framehash exit0을 확인했다. video900 frames 중896개 decoded picture hash가 서로 달랐다. 0/15/29초의 샘플 이미지를 추출·시각 검사해 캐릭터/박스 위치·자막·배경의 차이도 확인했다. 연속 player 재생·음성 청취·A/V 동기 확인과는 구별한다.

알려진 재검증CLI 실패2개와 최종CLI의 job 접두사 작업 폴더는모두0, 최종 staging도0이다. 별도 보존 source·framehash·샘플 이미지는 ignored 진단 자료이며 cleanup잔존이 아니다.

## 회귀와 인계

솔루션 build 경고/오류0, Core143 / Media Infrastructure186 / Downloader856개·총1185개 실패/skip0을 확인했다. 정확한 SEI/intra/header와 잘못된 proof·framing/IDR/inter 거절, 같은 native packet에 대한 증거 연결·raw flag 보존, 획득/shared round-trip을 테스트했다. 기존 Fast IDR·취소·종료/파일 소유·PCM·native/output 회귀는 유지한다.

원시 진단은 `.artifacts/motion-sync-diagnosis/ca33931448e7437a8070793089493e0d/`, `.artifacts/motion-target-sync/1c71cd3961464f69984f8604bfdd3909/`, final/framehash는 `.artifacts/motion-final-verification/a1eee5a982724de5966d7da7bd77dc3c/`다. 안전한 영속 관찰은 [JSON 증거](evidence/PHASE_1_H264_RECOVERY_POINT_SUPPORT.json)를 따른다. signed URL/headers/원시metadata JSON은 기록하지 않는다.

이 영상/포맷/구간의 Accurate 생성 검증은 완료했다. 일반 open GOP/모든 recovery SEI/다른 codec나구간·remote 전체 동일성을 확정하지 않는다. 직접 player 확인과 Phase1 전체 Passed/Completion/Tag는 Pending이며 Phase2/WPF 다운로드 UI/ClipEditor는 진행하지 않았다.
