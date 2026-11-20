import { Component, ReactNode } from "react";

/**
 * Signs+ arayüzündeki bir hata oyunun geri kalan arayüzünü (ve diğer modların pencerelerini) düşürmesin diye
 * pencere bu sınırın içinde çizilir. Hata olursa yalnızca Signs+ kendi penceresini kaldırır ve nedeni UI.log'a yazar.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; onError?: () => void }, { failed: boolean }> {
  constructor(props: any) {
    super(props);
    this.state = { failed: false };
  }
  static getDerivedStateFromError() {
    return { failed: true };
  }
  componentDidCatch(e: any) {
    console.log("[Signs+] UI error: " + ((e && e.stack) || e));
    try { if (this.props.onError) this.props.onError(); } catch (x) { /* yok say */ }
  }
  render() {
    return this.state.failed ? null : this.props.children;
  }
}
