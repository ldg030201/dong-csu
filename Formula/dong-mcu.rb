class DongMcu < Formula
  desc "Claude 사용량을 화면 위에 항상 띄워두는 macOS HUD"
  homepage "https://github.com/ldg030201/dong-mcu"
  url "https://github.com/ldg030201/dong-mcu/archive/refs/tags/v0.1.0.tar.gz"
  sha256 "a34bed352ae44acb4cb2cb773ff1e35734f1c962ae1ddc6cfd2ed59710e55786"
  license "MIT"
  head "https://github.com/ldg030201/dong-mcu.git", branch: "main"

  depends_on macos: :sonoma

  def install
    system "./build.sh"
    prefix.install "build/dong-mcu.app"
    bin.install_symlink prefix/"dong-mcu.app/Contents/MacOS/dong-mcu"
  end

  def caveats
    <<~EOS
      dong-mcu는 Claude Code가 keychain에 저장한 OAuth 토큰을 읽습니다.
      Claude Code에 로그인되어 있어야 동작하고, 첫 실행 때 keychain 접근 허용을 한 번 묻습니다.

      실행:
        open #{opt_prefix}/dong-mcu.app

      Launchpad와 /Applications 에서 보이게 하려면:
        ln -sfn #{opt_prefix}/dong-mcu.app /Applications/dong-mcu.app

      로그인하면 자동으로 시작하게 하려면 시스템 설정 > 일반 > 로그인 항목에 추가하세요.
    EOS
  end

  test do
    assert_match "dong-mcu", shell_output("#{bin}/dong-mcu --version")
  end
end
