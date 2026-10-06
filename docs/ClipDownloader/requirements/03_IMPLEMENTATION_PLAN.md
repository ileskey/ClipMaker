# ClipDownloader Implementation Plan & Acceptance Criteria

이 문서는 Codex가 기능을 단계적으로 구현할 때 따라야 할 작업 순서와 각 단계의 완료 조건을 정의한다.

한 번에 전체 프로젝트를 구현하지 말고 Phase 단위로 진행한다.

각 Phase 완료 후:

1. 빌드 성공 확인
2. 테스트 실행
3. 구현 내용 요약
4. 미완성 항목 표시
5. 다음 Phase에 영향을 주는 설계 결정 기록

을 수행한다.

---

# Phase 0 — Solution Bootstrap

## 목표

기본 솔루션과 실행 가능한 WPF 프로젝트를 만든다.

## 구현

- .NET WPF solution 생성
- 기본 MVVM 구조
- DI 사용 여부 결정
- 기본 logging 준비
- tools 경로 abstraction 준비

## 완료 조건

- Solution build 성공
- WPF 창 실행 가능
- Core / Application / Infrastructure 경계 생성
- 테스트 프로젝트 실행 가능

---

# Phase 1 — CLI/Core Prototype

## 목표

UI보다 먼저 핵심 기능을 검증한다.

입력:

- URL
- Start
- End
- Output path

출력:

- MP4 clip

## 구현

### 1. Timecode

- `HH:MM:SS:FF` parser
- validation
- TimeSpan 변환

### 2. ProcessRunner

- 외부 process 실행
- stdout/stderr capture
- cancellation

### 3. yt-dlp metadata

- URL → JSON
- VideoInfo 변환

### 4. clip extraction

yt-dlp + FFmpeg를 이용하여 지정 구간을 생성한다.

## 완료 조건

실제 테스트 기준:

### CHZZK

CHZZK VOD URL에서 지정한 10~30초 구간이 MP4로 생성된다.

### YouTube

YouTube URL에서 지정한 10~30초 구간이 MP4로 생성된다.

또한:

- 영상/음성이 정상 재생
- 취소 가능
- invalid URL 오류 처리
- yt-dlp 누락 오류 처리
- FFmpeg 누락 오류 처리

---

# Phase 2 — WPF MVP

## 목표

실제로 사용할 수 있는 최소 UI를 만든다.

## UI

필수:

```text
URL
[                                ]

[영상 불러오기]

Start
[00]:[00]:[00]:[00]

End
[00]:[00]:[00]:[00]

Quality
[Best ▼]

Mode
(•) Fast
( ) Accurate

Output
[C:\...\clip.mp4] [찾아보기]

[다운로드]

Progress
[████████----------]

Status
Downloading...
```

## 동작

1. URL 입력
2. Metadata 조회
3. 타임코드 입력
4. 다운로드 실행
5. progress 표시
6. 완료 표시

## 완료 조건

- UI freeze 없음
- 작업 취소 가능
- CHZZK 동작
- YouTube 동작
- Start / End validation
- output path validation

---

# Phase 3 — Metadata UI

## 목표

다운로드 전 사용자가 영상 정보를 확인할 수 있게 한다.

## 표시

- Title
- Uploader
- Duration
- FPS
- Thumbnail
- Available resolutions

## 완료 조건

URL을 불러온 뒤 최소한 다음이 표시됨:

```text
Title
Duration
FPS
Resolution
```

실패 시 기존 metadata state를 초기화한다.

---

# Phase 4 — Multi-Clip Queue

## 목표

한 VOD에서 여러 구간을 추출할 수 있게 한다.

## UI 예

```text
Segments

1. Intro
   00:03:14:02 → 00:03:38:15

2. Fight
   00:17:21:11 → 00:19:42:02

3. Ending
   01:02:12:31 → 01:03:10:08

[+ Add]
[Remove]
[Download All]
```

## 기능

- Add
- Edit
- Delete
- Rename
- Download selected
- Download all

## 완료 조건

하나의 URL에서 3개 이상의 클립을 순차적으로 생성 가능.

각 작업의 실패가 전체 프로그램 crash로 이어지지 않아야 한다.

---

# Phase 5 — Preview Player

## 목표

직접 숫자를 입력하지 않고 영상을 보면서 IN/OUT을 지정한다.

## 기능

- Play/Pause
- Seek
- Current Timecode
- Frame Step
- Mark IN
- Mark OUT
- Add Segment

## 단축키

- Space: Play/Pause
- Left: Backward seek
- Right: Forward seek
- `,`: previous frame
- `.`: next frame
- I: Mark IN
- O: Mark OUT

## 주의

이 Phase 전에는 Player 기술을 선택하기 위해 별도 spike를 수행해도 된다.

후보 예:

- LibVLCSharp
- FFmpeg 기반 custom preview
- 기타 WPF-compatible player

Player 선택이 전체 핵심 도메인 모델을 변경하지 않도록 한다.

---

# Phase 6 — Browser Integration

## 목표

브라우저에서 현재 보고 있는 영상을 ClipDownloader로 전달한다.

## 1차 구현

Custom URI Scheme:

```text
clipdownloader://open?url={encoded-url}
```

Chrome extension:

```text
[Open in ClipDownloader]
```

## 향후

현재 재생 위치 전달:

```text
clipdownloader://open
?url=...
&time=1234.52
```

## 완료 조건

CHZZK 또는 YouTube 페이지에서 버튼 클릭 시 Windows 앱이 열리고 URL이 자동 입력된다.

---

# Phase 7 — Local File Support

## 목표

온라인 다운로드 기능과 Clip Processor를 분리한다.

지원:

- mp4
- mkv
- mov 등 FFmpeg 지원 파일

흐름:

```text
Local File
    ↓
Media Info
    ↓
Clip Segment
    ↓
FFmpeg
    ↓
Output
```

## 완료 조건

같은 ClipSegment / Timecode 모델을 온라인 URL과 로컬 파일 양쪽에서 재사용한다.

---

# Phase 8 — Editor Integration Preparation

## 목표

향후 키리누키 편집기에 모듈로 편입할 수 있도록 public API 경계를 정리한다.

최종 구조 목표:

```text
OnlineSource
      ↓
MediaImporter
      ↓
LocalCache
      ↓
ClipProcessor
      ↓
Editor

LocalFile
      ↓
MediaImporter
      ↓
ClipProcessor
      ↓
Editor
```

UI 전용 코드 없이도 Application Layer에서 import / clip 기능을 호출할 수 있어야 한다.

---

# Definition of Done

각 기능은 다음을 만족해야 완료로 본다.

- Build 성공
- 관련 테스트 성공
- UI freeze 없음
- Cancellation 동작
- 오류 메시지 사용자 친화적
- raw exception/log는 debug/log에 보존
- ViewModel이 외부 CLI command 세부사항을 알지 않음
- Infrastructure 변경이 UI 변경을 강제하지 않음
- 주요 public method에 의미 있는 이름 사용
- 불필요한 abstraction을 미리 만들지 않음

---

# Codex 작업 지침

Codex는 아래 규칙을 따른다.

1. 한 Phase씩 구현한다.
2. 필요 이상의 기능을 선행 구현하지 않는다.
3. 현재 Phase 밖의 리팩터링은 최소화한다.
4. build/test를 실제로 실행한다.
5. 실패하면 원인을 확인하고 수정한다.
6. 실제 명령 결과를 확인하지 않고 "완료"라고 선언하지 않는다.
7. 큰 구조 변경 전 기존 요구사항과 충돌 여부를 확인한다.
8. TODO를 남길 경우 이유와 다음 처리 시점을 명시한다.
9. 사용자가 명시하지 않은 클라우드 서비스나 서버 의존성을 추가하지 않는다.
10. DRM 우회 기능은 구현하지 않는다.

---

# 우선순위

개발 우선순위:

1. 정확성
2. 안정성
3. 사용자 경험
4. 성능 최적화
5. 확장성

초기 단계에서 과도한 최적화보다 실제 CHZZK / YouTube 클립 생성 성공을 우선한다.
