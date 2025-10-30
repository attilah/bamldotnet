client<llm> TestClient {
  provider openai
  options {
    model "gpt-4o-mini"
    api_key env.OPENAI_API_KEY
  }
}

function SimpleTest(input: string) -> string {
  client TestClient
  prompt #"
    Echo this back: {{input}}
  "#
}
