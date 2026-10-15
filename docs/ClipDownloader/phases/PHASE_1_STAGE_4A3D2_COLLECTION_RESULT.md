# Phase 1 4a-3d-2 indexed byte-reference collection

실행일: 2026-10-14 (Asia/Seoul). 시작 HEAD `9a593ca14c696ecda9c2fd676bb25e3ab1925af4`, `phase/1`, clean. Base는 유지했다.

상태: **SIDX 직접 참조의 bounded HTTP 수집·소유 파일 구현 및 단위 검증 완료**. [SIDX reader](PHASE_1_STAGE_4A3D1_SIDX_RESULT.md)의 index와 같은 prefix 조회에서 나온 locator/resource length를 caller가 전달하는 내부 계약이다. public YouTube inspection/acquire 연결이나 전체 4a 완료는 아니다.

- 요청 index 구간에 겹치는 whole references를 선택한다. 최대 64개, 실제 선택 span 및 누적 byte 예산을 전송 전에 검사한다. prefix body는 이미 소비한 예산이며 init 복사 비용을 중복 청구하지 않는다.
- 각 참조를 명시 HeaderRange로 한 번 읽는다. 정확한 206 범위/완전 body는 기존 reader가 확인하고, collector는 최초 resource length와 일치하는지 추가 검사한다. 길이 일치는 동일 내용 증명이 아니다. 자동 query fallback/전체 영상 전송/재선택은 없다.
- CreateNew 파일에 ftyp/moov 및 선택한 참조만 순서대로 기록한다. SHA256·파일 bytes·총 소비 bytes와 요청/실제 **index** 구간을 보존한다. movie/native 원점·coverage·decoder 준비는 확정하지 않는다.
- 실패/취소에는 Data=null이다. 생성한 파일만 닫고 삭제하며, 닫기/삭제 실패 시 RemainingFiles로 인계한다. 기존 동일 경로 및 사용자 파일을 보존한다. 새 파일 수집 실패 후 자동 retry는 없다.

검증: Downloader Infrastructure tests `--no-restore` 581 통과/0 실패/0 skip, 솔루션 build `--no-restore` 경고0/오류0. 새 7개 사례는 정확한 두 byte 범위/해시/예산, Range 무시, 원본 길이 변경, 짧은 body, 전송 전 budget 거절, 선행 취소, CreateNew 충돌과 사용자 파일 보존을 확인했다. 첫 실행의 xUnit1051 경고 두 건은 테스트 파일 읽기에 test cancellation token을 전달해 수정했고 재실행에서 경고가 없었다.

이번 단위는 synthetic HTTP 및 실제 로컬 파일 I/O 검증이다. 새 원격 미디어 수집 실험은 실행하지 않았다. close/delete fault 주입, 전송 중 취소 및 indexed public pipeline 통합 검증은 아직 남았다. 기존 HLS 테스트 결과를 indexed 검증으로 합산하지 않는다. Core/Media.Infrastructure 전체 테스트는 재실행하지 않았으며 WPF는 빌드만 확인했다.

다음은 prefix/index/locator의 한 operation 내 연결, indexed 파일의 local probe, movie/edit/native bridge 및 분리 A/V 관계 검증이다. index0을 source O로 가정하지 않는다. 4b/4c·Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다.

커밋 단위: collector 1, 테스트 1, 결과/설계/source 계약/baseline 4문서. 메시지 `feat: Collect bounded indexed media references into owned files`.
