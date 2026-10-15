# Phase 1 후속 — Fast 분수 시작 좌표 지원

2026-10-15 (Asia/Seoul). 시작 HEAD `4e51de8`, branch `phase/1`. 사용자가 `qnjEE-wkw8k` / best137+140 / 30fps / Fast / `[10,40)`에서 보고한 `CopySeekClockUnsupported`를 진단했다. 기존 사용자 영상 파일과 채택한 제한 획득·Fast 정책을 보존한다.

## 실제 원인과 변경

같은 선택 원본을 64MiB/60초 안에서 획득하고 native/shared probe·random-access·qualifier를 실행했다. video1136 packets/frames의 관찰 범위는 `[68/15,212/5)`, audio1829개는 `[0,468224/11025)`였다. 선택된 시작점 K는 `68/15`초로 실제 H.264 폐쇄형 IDR 검사에 통과했다. 공통 movie clock11289600, 정확한 공통 translation `-68/15`, 두 stream의 계획 범위 `[68/15,40)`, 필수 video frames900개다.

기존 binder는 입력 `-ss`를 마이크로초로 **정확히** 변환하도록 요구했다. `68/15 * 1000000`은 정수가 아니므로 FFmpeg 실행 전에 거절했다. 이번 실패는 이전 recovery-point 바이트 관찰 오류와 다르며 Fast IDR 조건을 완화할 이유가 아니다.

`FastCopyCommandBinder`의 시작 **명령 좌표**만 Floor로 변환한다. 이 사례는 `-ss 4.533333`으로 K보다 정확히 `1/3000000`초 앞이며 차이는 1마이크로초 미만이다. OUT은 기존 Ceiling을 유지한다. native range·translation·required frame 기대값·공통 clock과 setts 정수 이동은 그대로다. 음수·Int64 범위 초과와 나머지 clock/codec/stream 표현 거절 조건도 유지한다.

[FFmpeg8.1 demux 구현](https://github.com/FFmpeg/FFmpeg/blob/n8.1/fftools/ffmpeg_demux.c#L1937)은 입력 시작 시간을 seek에 사용하며 컨테이너별 동작도 적용한다. Floor가 모든 입력에서 같은 packet을 선택한다고 추정하지 않는다. 기존 output native/packet PTS·DTS·duration·payload·필수 frame·decoded picture 검증을 모두 통과해야만 최종 파일을 확정한다. 자동 Accurate 전환·재인코딩·획득 예산 확대는 없다.

## 실제 CLI와 독립 검증

같은 URL/best/Fast/10~40초를 별도 ignored 출력에 실행해 exit0을 확인했다. job `22e6df40-9c65-4579-a85f-f58bca7b1e2a`, final `.artifacts/motion-accurate-cli/31c90c7ad075400b8ae92e7361c70edf/fast.mp4`,1584743 bytes·SHA256 `7b30c8cb2b91ec52e71395f1a5d03636f5634af2a2a7965706a52670cf01695a`다. 디렉터리 이름은 이전 harness 이름이며 이번 실행 mode는 Fast다.

- 요청 `[10,40)`초.
- 실제 video `[68/15,1201/30)`초, audio `[68/15,441088/11025)`초.
- AAC skip240/tail0, zero삽입0/중복제거0, SourceEndVerified=false.
- 독립 FFmpeg 전체 A/V decode exit0.
- 실제 native job root에서 해당 job ID 접두사 폴더0, 출력 디렉터리 staging0.

Fast는 앞선 닫힌 시작점을 사용하므로 이 결과는 요청보다 약5.47초 일찍 시작한다. 표시한 실제 범위를 요청과 같다고 표현하지 않는다. 생성 검증 시점에는 이번 Fast의 직접 player 확인이 Pending이었다. Phase1 전체 Passed/Completion/Tag는 Pending이다.

## 사용자 재생·동기 확인

2026-10-15 사용자가 같은 URL/137+140/30fps/Fast/10~40초를 `out5.mp4`로 실행한 완료 로그와 함께 “fast 검증했어. 성공했고, 음성 싱크도 맞아”라고 보고했다. 사용자 재생 성공 및 음성 동기 통과로 기록한다. 보고된 실제 video/audio 범위와 AAC skip240/tail0·zero0/overlap0·SourceEndVerified=false는 위 에이전트 실행 결과와 동일하다. 사용자 출력 파일의 hash 대조나 에이전트의 직접 청취 결과로 표현하지 않는다.

이 사례는 앞선 같은 영상 Accurate 사용자 통과 보고와 함께 YouTube 양 모드의 사용자 확인 증거다. 다른 source/구간 또는 기존 네 final 전체의 검증으로 확대하지 않는다. CHZZK Fast/Accurate의 사용자 재생·동기 확인과 Phase1 전체 완료 판정은 남아 있다.

## 회귀와 인계

명령 좌표 Floor/OUT Ceiling·정확한 원본 계획/translation 보존을 분수5개와 음수/범위 초과2개에서 검사한다. 최초 테스트 빌드는 SignedMediaTime의 없는 단항 연산자 때문에 실패했고 수정했다. 첫 suite의 두 거절 테스트는 필수 frame이 빠진 fixture 오류로 실패해 유효 계획으로 수정했다. 이 중간 실패를 성공으로 기록하지 않는다.

최종 솔루션 build 경고/오류0, Core143 / Media Infrastructure193 / Downloader856개·총1192개 실패/skip0을 확인했다. 최종 제품 소스는 실제 CLI 검증과 동일하며 이후 변경은 테스트 fixture와 문서뿐이다.

안전한 영속 관찰은 [JSON 증거](evidence/PHASE_1_FAST_FRACTIONAL_SEEK_SUPPORT.json)를 따른다. 원시 signed URL·headers·metadata는 기록하지 않는다. 최초 진단 자료는 `.artifacts/source-service-real/24f6c33fef554ab590638b971a90d6bd`에 보존한다. Phase2/WPF 다운로드 UI/ClipEditor와 baseline ancestry를 변경하지 않았다.
